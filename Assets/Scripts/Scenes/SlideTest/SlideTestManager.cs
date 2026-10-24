using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using MajdataPlay.Scenes.Game.Parsing;
using MajdataPlay.Scenes.Game.Notes.Slide.Utils;
using MajdataPlay.Settings;
using MajSimai;
using System;
using System.Globalization;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

#nullable enable
namespace MajdataPlay.Scenes.SlideTest
{
    /// <summary>
    /// Drives the slide test scene: builds a one-note chart from simai text, from slide code or from a predefined slide
    /// table, loads it through the game's note loader and plays it on its own clock, which can be paused, stepped by
    /// frames, slowed down and looped. It stands in for the game play manager as the note controller of the scene.
    /// </summary>
    /// <remarks>
    /// The global managers come from the Init scene. When Play Mode starts in this scene, the manager disables the rest
    /// of the scene, plays the Init scene and has the game manager load this scene again in view mode. The note
    /// components of a scene load only one chart, so every later generation reloads the scene and carries the settings
    /// of the manager over to the new one.
    /// </remarks>
    [DefaultExecutionOrder(-10000)]
    internal sealed class SlideTestManager : MajComponent, INoteController
    {
        /// <summary>The table name that stands for the wifi table, which is not one of the predefined slide shapes.</summary>
        internal const string WifiTableName = "wifi";

        /// <summary>Where the slides come from.</summary>
        [SerializeField]
        [FormerlySerializedAs("Source")]
        private SlideTestSource _source = SlideTestSource.PredefinedTable;

        /// <summary>The simai notes used when <see cref="_source"/> is <see cref="SlideTestSource.Simai"/>, for example <c>1-5[4:1]</c>.</summary>
        [SerializeField]
        [FormerlySerializedAs("Simai")]
        [TextArea(3, 12)]
        private string _simai = "1-5[4:1]";

        /// <summary>
        /// Whether simai slides are translated into slide code and built as extend slides, as the game does, instead of
        /// being built natively from the slide prefabs.
        /// </summary>
        [SerializeField]
        [FormerlySerializedAs("IsSimaiSlideTranslated")]
        private bool _isSimaiSlideTranslated = true;

        /// <summary>The slide code used when <see cref="_source"/> is <see cref="SlideTestSource.SlideCode"/>, for example <c>1P6K7</c>.</summary>
        [SerializeField]
        [FormerlySerializedAs("SlideCode")]
        private string _slideCode = "1P6K7";

        /// <summary>The predefined table, or <see cref="WifiTableName"/>, used when <see cref="_source"/> is <see cref="SlideTestSource.PredefinedTable"/>.</summary>
        [SerializeField]
        [FormerlySerializedAs("TableName")]
        private string _tableName = "line3";

        /// <summary>The key the predefined table shape starts at.</summary>
        [SerializeField]
        [FormerlySerializedAs("StartKey")]
        [UnityEngine.Range(1, 8)]
        private int _startKey = 1;

        /// <summary>Whether the predefined table shape is mirrored about the line through its start key and the centre.</summary>
        [SerializeField]
        [FormerlySerializedAs("IsMirrored")]
        private bool _isMirrored;

        /// <summary>The simai duration appended to slide code and to predefined table shapes, for example <c>[4:1]</c>.</summary>
        [SerializeField]
        [FormerlySerializedAs("Duration")]
        private string _duration = "[4:1]";

        /// <summary>The tempo of the generated chart.</summary>
        [SerializeField]
        [FormerlySerializedAs("Bpm")]
        [Min(1f)]
        private float _bpm = 120f;

        /// <summary>The seconds the clock starts before the note, so that the slide fades in.</summary>
        [SerializeField]
        [FormerlySerializedAs("LeadInSec")]
        [Min(0f)]
        private float _leadInSec = 2f;

        /// <summary>The seconds the clock keeps running after the last note ends.</summary>
        [SerializeField]
        [FormerlySerializedAs("TailSec")]
        [Min(0f)]
        private float _tailSec = 1f;

        /// <summary>How fast the clock runs compared with real time.</summary>
        [SerializeField]
        [FormerlySerializedAs("PlaybackSpeed")]
        [UnityEngine.Range(0.05f, 2f)]
        private float _playbackSpeed = 1f;

        /// <summary>The autoplay mode of the generated notes; changing it takes effect on the next generation.</summary>
        [SerializeField]
        [FormerlySerializedAs("Autoplay")]
        private AutoplayModeOption _autoplay = AutoplayModeOption.Enable;

        /// <summary>
        /// Whether the chart is generated again once the clock passes its end. Every generation reloads the scene, so a
        /// slide selected in the inspector is replaced.
        /// </summary>
        [SerializeField]
        [FormerlySerializedAs("IsLooping")]
        private bool _isLooping;

        /// <summary>Whether the chart is generated when the scene starts.</summary>
        [SerializeField]
        [FormerlySerializedAs("IsGeneratingOnStart")]
        private bool _isGeneratingOnStart = true;

        /// <summary>The object shown while the chart loads; optional.</summary>
        [SerializeField]
        [FormerlySerializedAs("LoadingIndicator")]
        private GameObject? _loadingIndicator;

        /// <summary>
        /// The settings of the manager that reloaded the scene to generate a chart, as JSON, or <see langword="null"/>
        /// when the scene was not reloaded for a generation.
        /// </summary>
        private static string? s_settingsToRestore;

        /// <summary>Whether the manager that reloaded the scene was selected in the editor, so that the new one is selected too.</summary>
        private static bool s_wasSelected;

        /// <summary>The state of the test clock.</summary>
        private SlideTestState _state = SlideTestState.Idle;

        /// <summary>Whether this scene instance has started loading a chart; the note components load only one chart.</summary>
        private bool _hasLoadedChart;

        /// <summary>Whether the scene was reloaded to generate a chart with restored settings.</summary>
        private bool _isGenerationRequested;

        /// <summary>The chart time of this frame in seconds.</summary>
        private float _thisFrameSec;

        /// <summary>The chart time the last note ends at.</summary>
        private float _endSec;

        /// <summary>The seconds the next update advances the clock by while paused.</summary>
        private float _pendingStepSec;

        /// <summary>Whether the notes were updated in this frame, so that their late update runs too.</summary>
        private bool _isUpdatedThisFrame;

        /// <summary>Whether the scene only hands over to the Init scene and must stay inactive.</summary>
        private bool _isBootstrapping;

        /// <summary>The note loader of the scene.</summary>
        private NoteLoader _noteLoader = null!;

        /// <summary>The note manager of the scene.</summary>
        private NoteManager _noteManager = null!;

        /// <summary>The note pool manager of the scene.</summary>
        private NotePoolManager _notePoolManager = null!;

        /// <summary>The note audio manager of the scene.</summary>
        private NoteAudioManager _noteAudioManager = null!;

        /// <summary>Gets the length of the chart, which has no audio, from its start to the end of the tail.</summary>
        public float AudioLength => _endSec + _tailSec;

        /// <summary>Gets whether the notes are running, including while the clock is paused or stepped.</summary>
        public bool IsStart => _state is SlideTestState.Playing or SlideTestState.Paused;

        /// <summary>Gets whether autoplay plays the notes.</summary>
        public bool IsAutoplay => ModInfo.AutoPlay != AutoplayModeOption.Disable;

        /// <summary>Gets the mods of the generated notes: the user settings with the autoplay mode of this scene.</summary>
        public GameModInfo ModInfo { get; private set; }

        /// <summary>Gets the grade autoplay judges notes with.</summary>
        public JudgeGrade AutoplayGrade => JudgeGrade.Perfect;

        /// <summary>Gets the material of break notes.</summary>
        public Material BreakMaterial { get; private set; } = null!;

        /// <summary>Gets the material of ordinary notes.</summary>
        public Material DefaultMaterial { get; private set; } = null!;

        /// <summary>Gets the material that makes held holds shine.</summary>
        public Material HoldShineMaterial { get; private set; } = null!;

        /// <summary>Gets the chart time of this frame in seconds.</summary>
        public float ThisFrameSec => _thisFrameSec;

        /// <summary>Gets the state of the test clock.</summary>
        internal SlideTestState State => _state;

        /// <summary>Gets the chart time the last note ends at.</summary>
        internal float EndSec => _endSec;

        /// <summary>Gets the chart text of the latest generation.</summary>
        internal string Fumen { get; private set; } = string.Empty;

        /// <summary>Gets the error of the latest generation, or an empty string when it succeeded.</summary>
        internal string LastError { get; private set; } = string.Empty;

        /// <summary>
        /// Restores the settings of the manager that reloaded the scene and registers the manager as the note controller,
        /// or, when the global managers do not exist yet, disables the scene and plays the Init scene, which loads this
        /// scene again.
        /// </summary>
        protected override void Awake()
        {
            if (Majdata<GameManager>.Instance is null)
            {
                Bootstrap();
                return;
            }
            RestoreSettings();
            base.Awake();
            Majdata<SlideTestManager>.Instance = this;
            Majdata<INoteController>.Instance = this;
            Majdata<INoteTimeProvider>.Instance = this;
            ModInfo = new GameModInfo(MajEnv.Settings.Mod)
            {
                AutoPlay = _autoplay
            };
            DefaultMaterial = RuntimeDatabase.Note.DefaultMaterial;
            BreakMaterial = RuntimeDatabase.Note.BreakMaterial;
            HoldShineMaterial = RuntimeDatabase.Note.HoldShineMaterial;
        }

        /// <summary>Finds the note components of the scene and generates the chart when asked to.</summary>
        private void Start()
        {
            if (_isBootstrapping)
            {
                return;
            }
            _noteLoader = Majdata<NoteLoader>.Instance!;
            _noteManager = Majdata<NoteManager>.Instance!;
            _notePoolManager = Majdata<NotePoolManager>.Instance!;
            _noteAudioManager = Majdata<NoteAudioManager>.Instance!;
            var bgManager = Majdata<BGManager>.Instance;
            if (bgManager is not null)
            {
                bgManager.DisableVideo();
                bgManager.SetBackgroundPic(null);
            }
            SetLoadingIndicatorActive(false);
            if (_isGeneratingOnStart || _isGenerationRequested)
            {
                Generate();
            }
        }

        /// <summary>Advances the clock and updates the notes while playing, or by one step while paused.</summary>
        private void Update()
        {
            var isStepping = _state == SlideTestState.Paused && _pendingStepSec > 0f;
            if (_state != SlideTestState.Playing && !isStepping)
            {
                return;
            }
            _thisFrameSec += isStepping ? _pendingStepSec : Time.deltaTime * _playbackSpeed;
            _pendingStepSec = 0f;

            Majdata<TimeDisplayer>.Instance?.OnPreUpdate();
            _noteAudioManager.OnPreUpdate();
            _noteManager.OnPreUpdate();
            _notePoolManager.OnPreUpdate();
            Majdata<ObjectCounter>.Instance?.OnPreUpdate();
            _noteManager.OnUpdate();
            _isUpdatedThisFrame = true;
        }

        /// <summary>
        /// Updates the judgment effects while the notes run, and runs the late update of the notes updated in this frame,
        /// then loops or stops at the end of the chart.
        /// </summary>
        private void LateUpdate()
        {
            if (IsStart)
            {
                Majdata<NoteEffectPool>.Instance?.OnLateUpdate();
            }
            if (!_isUpdatedThisFrame)
            {
                return;
            }
            _isUpdatedThisFrame = false;
            _noteAudioManager.OnLateUpdate();
            _noteManager.OnLateUpdate();
            Majdata<ObjectCounter>.Instance?.OnLateUpdate();

            if (_state == SlideTestState.Playing && _thisFrameSec >= AudioLength)
            {
                if (_isLooping)
                {
                    Generate();
                }
                else
                {
                    _state = SlideTestState.Paused;
                }
            }
        }

        /// <summary>Releases the controller registrations.</summary>
        private void OnDestroy()
        {
            if (_isBootstrapping)
            {
                return;
            }
            Majdata<SlideTestManager>.Free();
            Majdata<INoteController>.Free();
            Majdata<INoteTimeProvider>.Free();
        }

        /// <summary>
        /// Generates the chart from the current settings; ignored while a chart loads. The note components of a scene
        /// load only one chart, so once one has been loaded the scene is reloaded with the current settings, and the new
        /// manager generates the chart.
        /// </summary>
        internal void Generate()
        {
            if (_state == SlideTestState.Loading)
            {
                return;
            }
            if (_hasLoadedChart)
            {
                ReloadToGenerate();
                return;
            }
            GenerateAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        /// <summary>Pauses a playing clock or resumes a paused one.</summary>
        internal void TogglePause()
        {
            switch (_state)
            {
                case SlideTestState.Playing:
                    _state = SlideTestState.Paused;
                    break;
                case SlideTestState.Paused:
                    _state = SlideTestState.Playing;
                    break;
            }
        }

        /// <summary>Pauses the clock and advances it by whole game frames in the next update.</summary>
        /// <param name="frameCount">How many frames of <see cref="MajEnv.FRAME_LENGTH_SEC"/> to advance by.</param>
        internal void StepFrames(int frameCount)
        {
            if (!IsStart)
            {
                return;
            }
            _state = SlideTestState.Paused;
            _pendingStepSec += frameCount * MajEnv.FRAME_LENGTH_SEC;
        }

        /// <summary>Gets the slides generated in the scene.</summary>
        /// <returns>The slides, in hierarchy order; empty before a chart is loaded.</returns>
        internal SlideBase[] FindSlides()
        {
            if (_noteLoader == null || _noteLoader.notes == null)
            {
                return Array.Empty<SlideBase>();
            }
            return _noteLoader.notes.GetComponentsInChildren<SlideBase>();
        }

        /// <summary>Builds the chart text of the current settings.</summary>
        /// <returns>A chart with one timing point holding the notes, for example <c>(120){4}1-5[4:1],E</c>.</returns>
        /// <exception cref="ArgumentException">Thrown when the predefined table does not exist or the slide code is invalid.</exception>
        internal string BuildFumen()
        {
            var notes = _source switch
            {
                SlideTestSource.Simai => _simai.Trim(),
                SlideTestSource.SlideCode => _slideCode.Trim() + _duration.Trim(),
                _ => BuildPredefinedTableNote()
            };
            return $"({_bpm.ToString(CultureInfo.InvariantCulture)}){{4}}{notes},E";
        }

        /// <summary>Builds the note of the predefined table shape, rotated to the start key and mirrored when asked to.</summary>
        /// <returns>The slide code with its duration, or a wifi slide in simai.</returns>
        /// <exception cref="ArgumentException">Thrown when no predefined table has the name.</exception>
        private string BuildPredefinedTableNote()
        {
            var duration = _duration.Trim();
            if (_tableName == WifiTableName)
            {
                var endKey = ((_startKey + 3) % 8) + 1;
                return $"{_startKey}w{endKey}{duration}";
            }
            foreach (var (name, slideCode) in SlideTables.PredefinedShapes)
            {
                if (name != _tableName)
                {
                    continue;
                }
                var code = _isMirrored ? SlideCodeShape.Mirror(slideCode) : slideCode;
                return SlideCodeShape.Rotate(code, _startKey - 1) + duration;
            }
            throw new ArgumentException($"No predefined slide table is named \"{_tableName}\".");
        }

        /// <summary>
        /// Waits one frame so that every component of the scene has started, then loads the chart and starts the clock
        /// before it.
        /// </summary>
        /// <param name="token">Cancels the generation when the manager is destroyed.</param>
        private async UniTaskVoid GenerateAsync(CancellationToken token)
        {
            _state = SlideTestState.Loading;
            LastError = string.Empty;
            SetLoadingIndicatorActive(true);
            try
            {
                // 本组件最先执行，先等一帧让场景里其他组件的 Start 都跑完（例如音符加载器在 Start 里取得音符管理器）
                await UniTask.Yield(PlayerLoopTiming.Update, token);

                Fumen = BuildFumen();
                var chart = SimaiParser.ParseChart(Fumen.AsSpan());
                _endSec = GetEndSec(chart);
                ModInfo = new GameModInfo(MajEnv.Settings.Mod)
                {
                    AutoPlay = _autoplay
                };
                var tapSpeed = Math.Abs(MajEnv.Settings.Game.TapSpeed);
                var noteSpeed = (float)(107.25 / (71.4184491 * Mathf.Pow(tapSpeed + 0.9975f, -0.985558604f)));
                _noteLoader.NoteSpeed = MajEnv.Settings.Game.TapSpeed < 0 ? -noteSpeed : noteSpeed;
                _noteLoader.TouchSpeed = MajEnv.Settings.Game.TouchSpeed;
                _noteLoader.IsSimaiSlideTranslated = _isSimaiSlideTranslated;
                Majdata<ChartVisualDisplayer>.Instance?.SetSimaiChart(chart, AudioLength);

                // 从这里起音符组件开始保存这张谱面的状态，之后再生成就要重新加载场景
                _hasLoadedChart = true;
                await _noteLoader.LoadNotesIntoPoolAsync(chart, token);
                // 与游戏场景相同，按谱面用到的键位和传感器创建判定特效
                Majdata<NoteEffectPool>.Instance?.Init();
                await _noteManager.InitAsync();
                await UniTask.SwitchToMainThread(token);

                _thisFrameSec = -_leadInSec;
                _state = SlideTestState.Playing;
                MajDebug.LogInfo(nameof(SlideTestManager), $"Generated \"{Fumen}\", ending at {_endSec:0.###} s");
            }
            catch (OperationCanceledException)
            {
                _state = SlideTestState.Idle;
            }
            catch (Exception e)
            {
                MajDebug.LogException(nameof(SlideTestManager), e);
                LastError = e.Message;
                _state = SlideTestState.Error;
            }
            finally
            {
                if (this != null)
                {
                    SetLoadingIndicatorActive(false);
                }
            }
        }

        /// <summary>
        /// Keeps the current settings and reloads the scene, whose new manager restores them and generates the chart.
        /// Reloading is the only way to reset the note components, which keep the state of the chart they loaded.
        /// </summary>
        private void ReloadToGenerate()
        {
            _state = SlideTestState.Loading;
            SetLoadingIndicatorActive(true);
            s_settingsToRestore = JsonUtility.ToJson(this);
            var scene = gameObject.scene;
#if UNITY_EDITOR
            s_wasSelected = UnityEditor.Selection.activeGameObject == gameObject;
            // 测试场景不在构建设置里，所以按路径加载
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(scene.name);
#endif
        }

        /// <summary>
        /// Restores the settings of the manager that reloaded the scene, keeping the object references of this scene, and
        /// requests the generation; selects this manager when the previous one was selected.
        /// </summary>
        private void RestoreSettings()
        {
            if (s_settingsToRestore is null)
            {
                return;
            }
            var loadingIndicator = _loadingIndicator;
            JsonUtility.FromJsonOverwrite(s_settingsToRestore, this);
            // 对象引用按实例 ID 保存，场景重新加载后就失效了，所以保留新场景里的引用
            _loadingIndicator = loadingIndicator;
            s_settingsToRestore = null;
            _isGenerationRequested = true;
#if UNITY_EDITOR
            if (s_wasSelected)
            {
                UnityEditor.Selection.activeGameObject = gameObject;
            }
#endif
            s_wasSelected = false;
        }

        /// <summary>Shows or hides the loading indicator when the scene has one.</summary>
        /// <param name="isActive">Whether the indicator is shown.</param>
        private void SetLoadingIndicatorActive(bool isActive)
        {
            if (_loadingIndicator != null && _loadingIndicator.activeSelf != isActive)
            {
                _loadingIndicator.SetActive(isActive);
            }
        }

        /// <summary>
        /// Disables the rest of the scene before it wakes up and plays the Init scene, which sets up the global managers
        /// and then loads this scene again.
        /// </summary>
        private void Bootstrap()
        {
            _isBootstrapping = true;
#if UNITY_EDITOR
            var scene = gameObject.scene;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root != gameObject)
                {
                    root.SetActive(false);
                }
            }
            GameManager.EditorReturnScenePath = scene.path;
            SceneManager.LoadScene(0);
#else
            MajDebug.LogError(nameof(SlideTestManager), "The slide test scene needs the global managers of the Init scene");
#endif
        }

        /// <summary>Gets the chart time the last note of a chart ends at.</summary>
        /// <param name="chart">The chart.</param>
        /// <returns>The latest end of a hold or a slide, or the latest note timing.</returns>
        private static float GetEndSec(SimaiChart chart)
        {
            var endSec = 0d;
            foreach (var timing in chart.NoteTimings)
            {
                foreach (var note in timing.Notes)
                {
                    endSec = Math.Max(endSec, timing.Timing + note.HoldTime);
                    endSec = Math.Max(endSec, note.SlideStartTime + note.SlideTime);
                }
            }
            return (float)endSec;
        }
    }
}
