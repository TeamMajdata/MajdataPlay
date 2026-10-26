#nullable enable
#if UNITY_ANDROID && DEVELOPMENT_BUILD && !UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Title;
using MajdataPlay.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MajdataPlay.Rendering
{
    /// <summary>Records complete gameplay and results across rendering configurations in an isolated player.</summary>
    public sealed class AndroidFullCaptureDriver : MonoBehaviour
    {
        /// <summary>Number of configurations compiled into this capture player.</summary>
#if GSC_FULL_CAPTURE_MATRIX
        private const int GroupCount = 8;
#elif GSC_FULL_CAPTURE_LEGACY
        private const int GroupCount = 3;
#else
        private const int GroupCount = 1;
#endif
        /// <summary>Stops dependent steps after an explicit validation failure.</summary>
        private bool _failed;
        /// <summary>The current one-based configuration number.</summary>
        private int _group;
        /// <summary>The requested sample count for the current configuration.</summary>
        private int _requestedSamples;
        /// <summary>Whether the configuration requires a final presentation copy.</summary>
        private bool _intermediate;
        /// <summary>The requested scene resolution percentage.</summary>
        private int _scale;
        /// <summary>The sample count observed in the current gameplay.</summary>
        private int _gameSamples;
        /// <summary>The number of completed configurations with fewer samples than requested.</summary>
        private int _fallbackGroups;

        /// <summary>Creates the lifetime-bound capture driver before scene initialization.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            var instance = new GameObject(nameof(AndroidFullCaptureDriver));
            DontDestroyOnLoad(instance);
            instance.AddComponent<AndroidFullCaptureDriver>();
        }

        /// <summary>Initializes once, then plays every configuration without resetting the production trace.</summary>
        /// <returns>The capture session coroutine.</returns>
        private IEnumerator Start()
        {
            if (IsManualCaptureRequested())
            {
                MajDebug.LogInfo(nameof(AndroidFullCaptureDriver), "GSC_FULL_CAPTURE_MANUAL: automatic scene control is disabled.");
                yield break;
            }
            StartCoroutine(MonitorManualCapture());
            MajDebug.LogInfo(nameof(AndroidFullCaptureDriver), $"GSC_FULL_CAPTURE_BEGIN groups={GroupCount}; api={SystemInfo.graphicsDeviceType}");
            var flag = typeof(TitleManager).GetField("_flag", BindingFlags.Instance | BindingFlags.NonPublic);
            var nextScene = typeof(TitleManager).GetMethod("NextScene", BindingFlags.Instance | BindingFlags.NonPublic);
            var deadline = Time.realtimeSinceStartup + 240f;
            TitleManager? title = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                title = FindFirstObjectByType<TitleManager>();
                if (title != null && flag?.GetValue(title) is true)
                {
                    break;
                }
                yield return new WaitForSecondsRealtime(1f);
            }
            if (title == null || flag?.GetValue(title) is not true || nextScene == null)
            {
                Fail("title initialization timed out or its transition hook is unavailable");
                yield break;
            }
            if (SongStorage.IsEmpty || !MajInstances.SkinManager.IsInited)
            {
                Fail("bundled charts or skin initialization are unavailable");
                yield break;
            }
            MajEnv.Settings.Mod.AutoPlay = AutoplayModeOption.Enable;
            MajEnv.Settings.Mod.PlaybackSpeed = 1f;
            MajEnv.Settings.Online.Enable = false;
            MajDebug.LogInfo(nameof(AndroidFullCaptureDriver), "GSC_FULL_CAPTURE_TITLE_READY");
            if (IsManualCaptureRequested())
            {
                yield break;
            }
            nextScene.Invoke(title, null);
            yield return WaitForScene("List", 45f);
            if (_failed)
            {
                yield break;
            }
            yield return new WaitForSecondsRealtime(8f);
            var requestedTitle = ReadCaptureSong();
            var selected = requestedTitle == null ? null : SelectChart(requestedTitle);
            if (selected == null)
            {
                Fail($"requested local Master chart is unavailable: {requestedTitle}");
                yield break;
            }
            for (_group = 1; _group <= GroupCount; _group++)
            {
                if (IsManualCaptureRequested())
                {
                    yield break;
                }
                ConfigureGroup();
                MajDebug.LogInfo(nameof(AndroidFullCaptureDriver),
                    $"GSC_GROUP_BEGIN group={_group}/{GroupCount}; requestedMSAA={_requestedSamples}; "
                    + $"output={(_intermediate ? "Intermediate" : "Direct")}; renderScale={_scale}; "
                    + $"compatibility={MajEnv.Settings.Rendering.CompatibilityMode}; quality={QualitySettings.names[QualitySettings.GetQualityLevel()]}; chart={selected.Title}; difficulty=Master");
                var startFrame = Time.frameCount;
                var startTime = Time.realtimeSinceStartup;
                Majdata<GameInfo>.Instance = new GameInfo(GameMode.Normal, new[] { selected }, new[] { ChartLevel.Master });
                MajInstances.SceneSwitcher.SwitchScene("Game", false);
                yield return WaitForScene("Game", 60f);
                if (_failed)
                {
                    yield break;
                }
                deadline = Time.realtimeSinceStartup + 90f;
                GamePlayManager? game = null;
                while (Time.realtimeSinceStartup < deadline)
                {
                    game = FindFirstObjectByType<GamePlayManager>();
                    if (game != null && game.IsStart && game.State == GamePlayStatus.Running)
                    {
                        break;
                    }
                    yield return new WaitForSecondsRealtime(1f);
                }
                if (game == null || !game.IsStart || game.State != GamePlayStatus.Running)
                {
                    Fail("gameplay did not enter Running");
                    yield break;
                }
                var audioLength = game.AudioLength;
                var playbackStart = Time.realtimeSinceStartup;
                if (audioLength <= 0f || float.IsNaN(audioLength) || float.IsInfinity(audioLength)
                    || Math.Abs(game.PlaybackSpeed - 1f) > 0.001f)
                {
                    Fail($"unexpected chart duration or speed: duration={audioLength:F2}; speed={game.PlaybackSpeed:F2}");
                    yield break;
                }
                yield return ObserveOutput("Game");
                if (_failed)
                {
                    yield break;
                }
                yield return WaitForScene("Result", audioLength + 90f);
                if (_failed)
                {
                    yield break;
                }
                var playbackSeconds = Time.realtimeSinceStartup - playbackStart;
                if (playbackSeconds < audioLength - 5f)
                {
                    Fail($"Result arrived before the complete chart: elapsed={playbackSeconds:F2}; audio={audioLength:F2}");
                    yield break;
                }
                yield return new WaitForSecondsRealtime(12f);
                yield return ObserveOutput("Result");
                if (_failed)
                {
                    yield break;
                }
                if (_gameSamples < _requestedSamples)
                {
                    _fallbackGroups++;
                }
                MajDebug.LogInfo(nameof(AndroidFullCaptureDriver),
                    $"GSC_GROUP_COMPLETE group={_group}/{GroupCount}; requestedMSAA={_requestedSamples}; actualMSAA={_gameSamples}; "
                    + $"fallback={_gameSamples < _requestedSamples}; output={MajRenderDiagnostics.OutputStatus}; "
                    + $"frames={Time.frameCount - startFrame}; elapsed={Time.realtimeSinceStartup - startTime:F2}; playback={playbackSeconds:F2}; audio={audioLength:F2}");
                if (IsManualCaptureRequested())
                {
                    yield break;
                }
                MajInstances.SceneSwitcher.SwitchScene("List", false);
                yield return WaitForScene("List", 45f);
                if (_failed)
                {
                    yield break;
                }
                yield return new WaitForSecondsRealtime(4f);
            }
            if (IsManualCaptureRequested())
            {
                yield break;
            }
            MajDebug.LogInfo(nameof(AndroidFullCaptureDriver),
                $"GSC_FULL_CAPTURE_COMPLETE groups={GroupCount}; fallbackGroups={_fallbackGroups}; finalSave=pending Application.quitting");
            Application.Quit();
        }

        /// <summary>Applies the current direct/intermediate and sample-count combination to the real render pipeline.</summary>
        private void ConfigureGroup()
        {
            var configuration = _group;
#if GSC_FULL_CAPTURE_LEGACY
            configuration = _group == 3 ? 6 : _group;
#endif
            _requestedSamples = 1 << ((configuration - 1) / 2);
            _intermediate = (configuration & 1) == 0;
            _scale = _intermediate && _requestedSamples == 1 ? 50 : _intermediate && _requestedSamples == 4 ? 150 : 100;
            var options = MajEnv.Settings.Rendering;
            options.RenderQuality = RenderingQualityOption.Custom;
            options.CompatibilityMode = _intermediate && _requestedSamples == 1;
            options.SystemResolutionScaling = 100;
            options.RenderScale = _scale;
            options.AntiAliasing = (RenderingAntialiasingOption)_requestedSamples;
            options.TextureQuality = RenderingTextureQualityOption.High;
            options.OutputMode = _intermediate ? RenderingOutputMode.Intermediate : RenderingOutputMode.Direct;
            GameManager.ApplyRenderingSettings();
        }

        /// <summary>Waits for a scene instead of accepting a previous Result as the current chart's completion.</summary>
        /// <param name="scene">The expected active scene.</param>
        /// <param name="timeout">Maximum wait in real-time seconds.</param>
        /// <returns>The scene wait coroutine.</returns>
        private IEnumerator WaitForScene(string scene, float timeout)
        {
            var deadline = Time.realtimeSinceStartup + timeout;
            var delay = new WaitForSecondsRealtime(1f);
            while (SceneManager.GetActiveScene().name != scene && Time.realtimeSinceStartup < deadline)
            {
                yield return delay;
            }
            if (SceneManager.GetActiveScene().name != scene)
            {
                Fail($"scene {scene} timed out; actual={SceneManager.GetActiveScene().name}");
            }
        }

        /// <summary>Observes completed frames and reads sample counts from created MRP attachments.</summary>
        /// <param name="stage">The gameplay or results stage being recorded.</param>
        /// <returns>The render validation coroutine.</returns>
        private IEnumerator ObserveOutput(string stage)
        {
            var deadline = Time.realtimeSinceStartup + 20f;
            var stableFrames = 0;
            var previous = MajRenderOutputStatus.Unavailable;
            var endOfFrame = new WaitForEndOfFrame();
            while (Time.realtimeSinceStartup < deadline && stableFrames < 30)
            {
                yield return endOfFrame;
                var status = MajRenderDiagnostics.OutputStatus;
                if (status is MajRenderOutputStatus.Pending or MajRenderOutputStatus.Unavailable or MajRenderOutputStatus.DirectScreenTransition)
                {
                    stableFrames = 0;
                    continue;
                }
                stableFrames = status == previous ? stableFrames + 1 : 1;
                previous = status;
            }
            var valid = _intermediate
                ? previous == (_scale == 100 ? MajRenderOutputStatus.IntermediateRequested : MajRenderOutputStatus.IntermediateScaled)
                : previous is MajRenderOutputStatus.Direct or MajRenderOutputStatus.DirectResolve
                    or MajRenderOutputStatus.IntermediateMSAAUnsupported or MajRenderOutputStatus.IntermediateNativeUnavailable
                    or MajRenderOutputStatus.IntermediateViewport;
            if (stableFrames < 30 || !valid)
            {
                Fail($"unstable or unexpected {stage} output: status={previous}; stableFrames={stableFrames}");
                yield break;
            }
            var actualSamples = ReadActualSamples(previous);
            if (actualSamples < 1 || actualSamples > _requestedSamples)
            {
                Fail($"could not validate actual {stage} attachments: samples={actualSamples}; requested={_requestedSamples}");
                yield break;
            }
            if (stage == "Game")
            {
                _gameSamples = actualSamples;
            }
            else if (actualSamples != _gameSamples)
            {
                Fail($"Result sample count changed: game={_gameSamples}; result={actualSamples}");
                yield break;
            }
            MajDebug.LogInfo(nameof(AndroidFullCaptureDriver),
                $"GSC_GROUP_STAGE group={_group}/{GroupCount}; scene={stage}; requestedMSAA={_requestedSamples}; actualMSAA={actualSamples}; "
                + $"requestedOutput={(_intermediate ? "Intermediate" : "Direct")}; actualOutput={previous}; "
                + $"outputFallback={!_intermediate && previous is not (MajRenderOutputStatus.Direct or MajRenderOutputStatus.DirectResolve)}; "
                + $"display={MajRenderDiagnostics.DisplayResolution}; sceneResolution={MajRenderDiagnostics.SceneResolution}; stableFrames={stableFrames}");
        }

        /// <summary>Reads allocated scene attachments, or the screen sample count for the direct path.</summary>
        /// <param name="status">The completed frame's presentation path.</param>
        /// <returns>The created attachment sample count, or zero when unavailable.</returns>
        private static int ReadActualSamples(MajRenderOutputStatus status)
        {
            if (status == MajRenderOutputStatus.Direct)
            {
                return Math.Max(1, Screen.msaaSamples);
            }
            var samples = 0;
            foreach (var texture in Resources.FindObjectsOfTypeAll<RenderTexture>())
            {
                if (texture.IsCreated() && (texture.name == "MRP Scene MSAA Color" || texture.name == "MRP Scene DepthStencil"))
                {
                    samples = Math.Max(samples, texture.antiAliasing);
                }
            }
            return samples;
        }

        /// <summary>Reads the exact requested title from the isolated package, defaulting to the requested bundled song.</summary>
        /// <returns>The requested title, or null if the optional selection file cannot be read.</returns>
        private static string? ReadCaptureSong()
        {
            try
            {
                var file = FileSystem.Directory.Open(Application.persistentDataPath)
                    .FindDirectory("GraphicsStates")?.FindFile("capture-song.txt");
                return file?.ReadAllText().Trim() ?? "月下繚乱 ~2022 Re:Master~";
            }
            catch (Exception exception)
            {
                MajDebug.LogException(nameof(AndroidFullCaptureDriver), exception);
                return null;
            }
        }

        /// <summary>Chooses the same requested complete chart for every matrix entry.</summary>
        /// <param name="title">The exact local song title, with no fallback to another song.</param>
        /// <returns>The matching local Master chart, or null if unavailable.</returns>
        private static ISongDetail? SelectChart(string title)
        {
            foreach (var collection in SongStorage.Collections)
            {
                foreach (var song in collection)
                {
                    if (!song.IsOnline && song.Title == title && HasMaster(song))
                    {
                        return song;
                    }
                }
            }
            return null;
        }

        /// <summary>Records a terminal failure without claiming a completed matrix.</summary>
        /// <param name="message">The failed condition and observed evidence.</param>
        private void Fail(string message)
        {
            _failed = true;
            MajDebug.LogError(nameof(AndroidFullCaptureDriver), $"GSC_FULL_CAPTURE_FAILED group={_group}/{GroupCount}: {message}");
        }

        /// <summary>Stops this test driver's coroutines when a manual capture marker appears.</summary>
        /// <returns>A polling coroutine that leaves production graphics tracing active.</returns>
        private IEnumerator MonitorManualCapture()
        {
            var delay = new WaitForSecondsRealtime(1f);
            while (true)
            {
                yield return delay;
                if (IsManualCaptureRequested())
                {
                    MajDebug.LogInfo(nameof(AndroidFullCaptureDriver), "GSC_FULL_CAPTURE_MANUAL: automatic scene control stopped.");
                    StopAllCoroutines();
                    yield break;
                }
            }
        }

        /// <summary>Checks the capture application's opt-out file without modifying user data.</summary>
        /// <returns>Whether manual mode was requested, or checking the marker failed.</returns>
        private static bool IsManualCaptureRequested()
        {
            try
            {
                var directory = FileSystem.Directory.Open(Application.persistentDataPath).FindDirectory("GraphicsStates");
                return directory?.FindFile("manual-capture") != null;
            }
            catch (Exception exception)
            {
                MajDebug.LogException(nameof(AndroidFullCaptureDriver), exception);
                return true;
            }
        }

        /// <summary>Checks for a Master difficulty without keeping a span across coroutine yields.</summary>
        /// <param name="song">The candidate bundled song.</param>
        /// <returns>Whether Master has a configured level.</returns>
        private static bool HasMaster(ISongDetail song)
        {
            return song.Levels.Length > (int)ChartLevel.Master && !string.IsNullOrWhiteSpace(song.Levels[(int)ChartLevel.Master]);
        }
    }
}
#endif
