#if UNITY_ANDROID && !UNITY_EDITOR
#define ANDROID_RUNTIME
#elif UNITY_IOS && !UNITY_EDITOR
#define IOS_RUNTIME
#elif UNITY_STANDALONE_WIN && !UNITY_EDITOR
#define WINDOWS_RUNTIME
#elif UNITY_STANDALONE_LINUX && !UNITY_EDITOR
#define LINUX_RUNTIME
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
#define OSX_RUNTIME
#endif
using AOT;
using Cysharp.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.i18n;
using MajdataPlay.IO;
using MajdataPlay.IO.Storage;
using MajdataPlay.Rendering;
using MajdataPlay.Scenes.Test;
using MajdataPlay.Settings;
using MajdataPlay.Timer;
using MajdataPlay.Syscall.Win32;
#if UNITY_STANDALONE_OSX
using MajdataPlay.Platform.MacOS;
#elif UNITY_IOS
using MajdataPlay.Platform.iOS;
#elif UNITY_ANDROID
using MajdataPlay.Platform.Android;
#endif
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;
using UnityEngine.Rendering;
using MajdataPlay.Diagnostics;
using MajdataPlay.Databases;
using MajdataPlay.Import;
using MajdataPlay.Recording;
using MajdataPlay.ResourceUpdate;
using System.Threading;
using MajdataPlay.Utils; // DO NOT REMOVE IT !!!

namespace MajdataPlay
{
#nullable enable
    [DontDestroyOnLoad]
    public sealed class GameManager : MajComponent
    {
        public static bool IsAppOnFocus { get; private set; } = true;
        public static event EventHandler<EventArgs?>? OnAppQuit;
        public static event EventHandler<EventArgs?>? OnSave;
        public static event EventHandler<bool>? OnAppFocus;
        public static event EventHandler<bool>? OnAppPause;

        public GameSetting Settings
        {
            get => MajEnv.Settings;
        }

        public bool IsInited { get; private set; }


        [SerializeField] private BuiltInTimeProvider _timer = BuiltInTimeProvider.Winapi;

        [SerializeField] private bool _isEnterView = false;
        [SerializeField] private bool _isEnterTest = false;
#if UNITY_EDITOR
        /// <summary>
        /// Gets or sets the path of a scene that Play Mode entered before the Init scene set up the global managers. Once
        /// they are set up the game loads the skins, which the title scene would load, and then that scene again in view
        /// mode instead of entering the title, so that editor-only scenes such as the slide test scene can be played
        /// directly.
        /// </summary>
        internal static string? EditorReturnScenePath { get; set; }
#endif

        private static readonly List<IntPtr> _windowHandles = new();
        private static readonly ReadOnlyMemory<ITimeProvider> _builtInTimeProviders = MajTimeline.BuiltInTimeProviders;

        protected override void Awake()
        {
            base.Awake();
            Majdata<GameManager>.SetAsSingleton(this);
            GameRuntime.Init();
        }
        private void Start()
        {
            StartInternal().Forget();
        }
        /// <summary>Extracts or checks the packaged resources of the current platform before any service reads them.</summary>
        /// <param name="progress">Receives the extraction and check state of each step, which the loading screen shows.</param>
        /// <param name="cancellationToken">Cancels the work, for example when the game manager is destroyed.</param>
        /// <returns>An operation that completes once the resources are ready; failures are logged so that startup continues.</returns>
        /// <remarks>
        /// Android and iOS extract the packaged resources on the first start and check them against the packaged manifests
        /// on later starts; Windows restores empty chart and skin roots. The Editor and other platforms read the packaged
        /// streaming assets directly.
        /// </remarks>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        private async UniTask CheckResourcesAsync(ProgressReporter progress, CancellationToken cancellationToken)
        {
            var budget = new FrameBudget();
            try
            {
#if ANDROID_RUNTIME || IOS_RUNTIME
                if (!MajEnv.AssetsDirectory.Exists)
                {
                    await MobileResourceImporter.ImportAsync(progress, budget, cancellationToken);
                }
                else
                {
                    await MobileResourceChecker.CheckAsync(progress, budget, cancellationToken);
                }
#elif WINDOWS_RUNTIME
                await DesktopResourceChecker.CheckAsync(progress, budget, cancellationToken);
#else
                await UniTask.CompletedTask;
#endif
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                MajDebug.LogError(nameof(GameManager), $"Resource check failed:\n{exception}");
            }
        }
        private async UniTask StartInternal()
        {
#if !UNITY_EDITOR
#if UNITY_IOS
            await UniTask.DelayFrame(2); // wait UnitySendMessage
#endif
#endif
            await UniTask.Yield();
            var sceneSwitcher = MajInstances.SceneSwitcher;
            await sceneSwitcher.FadeInAsync();
            // Show each extraction and check step on the loading screen as soon as it is reported.
            var resourceProgress = new ProgressReporter(report => sceneSwitcher.SetLoadingText(report.ToString()));
            try
            {
                await CheckResourcesAsync(resourceProgress, this.GetCancellationTokenOnDestroy());
            }
            finally
            {
                sceneSwitcher.SetLoadingText(string.Empty);
            }
            await ChartImporter.OnStartAsync();
#if UNITY_STANDALONE
            DiscordManager.Init();
#endif
            MajInstances.RuntimeInfoDisplayer.Init();
            MajInstances.AudioManager.Init();
            Localization.Init();
#if UNITY_STANDALONE_WIN
            _timer = BuiltInTimeProvider.Winapi;
#else
            _timer = BuiltInTimeProvider.Stopwatch;
#endif
            MajTimeline.TimeProvider = _builtInTimeProviders.Span[(int)_timer];
#if UNITY_STANDALONE
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
#endif

            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg == "--test-mode")
                {
                    MajEnv.Mode = RunningMode.Test;
                    break;
                }

                if (arg == "--view-mode")
                {
                    MajEnv.Mode = RunningMode.View;
                    Settings.Mod.AutoPlay = AutoplayModeOption.Enable;
                    break;
                }
            }

#if UNITY_EDITOR
            if (_isEnterTest)
            {
                MajEnv.Mode = RunningMode.Test;
            }
            else if (_isEnterView)
            {
                MajEnv.Mode = RunningMode.View;
                Settings.Mod.AutoPlay = AutoplayModeOption.Enable;
            }
#endif

            ApplyScreenConfig();
#if OSX_RUNTIME
            if (Settings.Debug.FullScreen && MajEnv.Mode != RunningMode.View)
            {
                await ApplyMacOSFullscreenAsync();
            }
#endif

            var availableLangs = Localization.Available;
            if (!availableLangs.IsEmpty())
            {
                if (string.IsNullOrEmpty(Settings.Display.Language))
                {
                    if (Localization.SetLangByCode(Application.systemLanguage.ToLocale()))
                    {
                        Settings.Display.Language = Localization.Current.ToString();
                    }
                }
                else
                {
                    var lang = availableLangs.Find(x => x.ToString() == Settings.Display.Language);
                    if (lang is null)
                    {
                        lang = availableLangs.First();
                        Settings.Display.Language = lang.ToString();
                    }

                    Localization.Current = lang;
                }
            }

            ApplyDisplaySettings();
#if UNITY_EDITOR || !(UNITY_ANDROID || UNITY_IOS)
            QualitySettings.vSyncCount = Settings.Rendering.VSync ? 1 : 0;
#endif
            QualitySettings.maxQueuedFrames = Settings.Debug.MaxQueuedFrames;
            DetectHWEncoder();
#if (!UNITY_EDITOR && UNITY_STANDALONE_WIN)
            if (Settings.Display.Topmost)
            {
                SetWindowTopmost();
            }
#endif
            IODetector.Init();
            InputManager.Init(Majdata<DummyTouchPanelRenderer>.Instance!.InstanceID2SensorIndexMappingTable);
            OutputManager.Init();
            Recorder.Init();

            IsInited = true;
#if UNITY_EDITOR
            if (EditorReturnScenePath is string returnScenePath)
            {
                EditorReturnScenePath = null;
                MajEnv.Mode = RunningMode.View;
                // 平时由标题场景加载皮肤，这里跳过了标题场景，所以先加载皮肤
                if (!MajInstances.SkinManager.IsInited)
                {
                    await MajInstances.SkinManager.InitAsync();
                }
                // 这个场景不一定在构建设置里，所以按路径加载
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(returnScenePath, new LoadSceneParameters(LoadSceneMode.Single));
                await MajInstances.SceneSwitcher.FadeOutAsync();
                return;
            }
#endif
            await UniTask.Delay(1000);
            if (MajEnv.Mode == RunningMode.Test)
            {
                EnterTestMode();
            }
            else if (MajEnv.Mode == RunningMode.View)
            {
                EnterView();
            }
            else
            {
                EnterTitle();
            }
        }

        private void DetectHWEncoder()
        {
            var deviceName = SystemInfo.graphicsDeviceName.ToLower();
            HardwareEncoder encoder;
            if (deviceName.Contains("nvidia"))
            {
                encoder = HardwareEncoder.NVENC;
            }
            else if (deviceName.Contains("amd"))
            {
                encoder = HardwareEncoder.AMF;
            }
            else if (deviceName.Contains("intel"))
            {
                encoder = HardwareEncoder.QSV;
            }
            else
            {
                encoder = HardwareEncoder.None;
            }

            MajEnv.HWEncoder = encoder;
        }
#if (!UNITY_EDITOR && UNITY_STANDALONE_WIN)
        void SetWindowTopmost()
        {

            Win32API.EnumWindows(EnumWindowsCallback, Process.GetCurrentProcess().Id);
            MajDebug.LogDebug($"Found window count: {_windowHandles.Count}");
            foreach (var handle in _windowHandles)
            {
                Win32API.SetWindowPos(handle, Win32API.HWND_TOPMOST, 0, 0, 0, 0, Win32API.SWP_NOMOVE | Win32API.SWP_NOSIZE);
            }

        }
#endif
#if UNITY_STANDALONE_WIN
        [MonoPInvokeCallback(typeof(Win32API.EnumWindowsProc))]
        private static bool EnumWindowsCallback(IntPtr hWnd, int lParam)
        {
            _windowHandles.Clear();
            Win32API.GetWindowThreadProcessId(hWnd, out int processId);

            if (processId == lParam && Win32API.IsWindowVisible(hWnd))
            {
                _windowHandles.Add(hWnd);
            }
            return true;
        }
#endif
        private void EnterTestMode()
        {
            IOListener.NextScene = "Title";
#if UNITY_STANDALONE_WIN && ENABLE_MONO
            MajEnv.GameProcess.PriorityClass = ProcessPriorityClass.AboveNormal;
#endif
            MajInstances.SceneSwitcher.SwitchScene("Test");
        }

        private void EnterTitle()
        {
#if UNITY_STANDALONE_WIN && ENABLE_MONO
            MajEnv.GameProcess.PriorityClass = ProcessPriorityClass.AboveNormal;
#endif
            MajInstances.SceneSwitcher.SwitchScene("Title");
        }

        private void EnterView()
        {
#if UNITY_STANDALONE_WIN && ENABLE_MONO
            MajEnv.GameProcess.PriorityClass = ProcessPriorityClass.BelowNormal;
#endif
            MajInstances.SceneSwitcher.SwitchScene("View");
        }
        private void ApplyDisplaySettings()
        {
            var renderingOptions = MajEnv.Settings.Rendering;
            ApplyRenderingSettings();
#if UNITY_STANDALONE || UNITY_EDITOR
            QualitySettings.vSyncCount = renderingOptions.VSync ? 1 : 0;
#endif
            Application.targetFrameRate = renderingOptions.FPSLimit;
        }

        internal static void ApplyRenderingSettings()
        {
            // Unity Quality 仅保留 Default 引擎基线；菜单预设不再索引多个质量资产。
            // 不为切换 MSAA / 纹理采样精度反复重建整个管线。
            const int UnityDefaultQualityLevel = 0;
            int baseline = Mathf.Min(UnityDefaultQualityLevel, QualitySettings.names.Length - 1);
            if (baseline >= 0 && QualitySettings.GetQualityLevel() != baseline)
                QualitySettings.SetQualityLevel(baseline, true);
            var parameters = MajEnv.Settings.Rendering.GetEffectiveParameters();
            // 保留完整 mip 链；纹理精度是采样 LOD 的 +0/+1/+2，而不是最高可用 mip 的下限。
            if (QualitySettings.globalTextureMipmapLimit != 0)
                QualitySettings.globalTextureMipmapLimit = 0;
            if (GraphicsSettings.currentRenderPipeline is MajRenderPipelineAsset pipeline)
                pipeline.ConfigureRuntimeRendering(parameters.RenderScale / 100f,
                    (MajMSAAMode)parameters.MSAASamples, parameters.ResolveToScreen, parameters.MipmapBias);
#if UNITY_ANDROID && !UNITY_EDITOR
            // 两个比例独立相乘；兼容模式暂时恢复原系统缓冲。
            MajAndroidDisplayResolution.Configure(GraphicsSettings.currentRenderPipeline is MajRenderPipelineAsset
                && !MajEnv.Settings.Rendering.CompatibilityMode
                ? MajEnv.Settings.Rendering.SystemResolutionScaling : 100);
#endif
#if UNITY_STANDALONE || UNITY_EDITOR
            // SetQualityLevel 会应用资产的 VSync 值，同步方式继续尊重独立显示选项。
            QualitySettings.vSyncCount = MajEnv.Settings.Rendering.VSync ? 1 : 0;
#endif
        }

        private void ApplyScreenConfig()
        {
            // Editor 的输出尺寸由 Game View / Device Simulator 决定，不能套用桌面保存的分辨率。
            // Simulator 会把请求的 9:16 画面铺到所选设备上，导致长屏纵向、宽屏横向拉伸。
            // 桌面 Player 保留原来的分辨率与全屏设置；MRP RenderScale 和 FPS 仍独立生效。
#if (UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX) && !UNITY_EDITOR
            if (MajEnv.Mode != RunningMode.View)
            {
                var fullScreen = Settings.Debug.FullScreen;
#if UNITY_STANDALONE_OSX
                Screen.fullScreen = fullScreen;
                Screen.fullScreenMode = fullScreen
                    ? FullScreenMode.FullScreenWindow
                    : FullScreenMode.Windowed;
#else
                Screen.fullScreen = fullScreen;
                Screen.fullScreenMode = FullScreenMode.ExclusiveFullScreen;
#endif

                var resolution = Settings.Display.Resolution.ToLower();
#if UNITY_STANDALONE_OSX
                if (!fullScreen && resolution is not "auto")
#else
                if (resolution is not "auto")
#endif
                {
                    var param = resolution.Split("x");
                    int width, height;

                    if (param.Length != 2)
                    {
                        return;
                    }
                    else if (!int.TryParse(param[0], out width) || !int.TryParse(param[1], out height))
                    {
                        return;
                    }
                    Screen.SetResolution(width, height, fullScreen);
                }
            }
#endif
            Application.targetFrameRate = Settings.Rendering.FPSLimit;
        }

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        async UniTask ApplyMacOSFullscreenAsync()
        {
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            NativePresentation.EnterNativeFullscreenIfNeeded();
        }
#endif

        private void Update()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            MajAndroidDisplayResolution.Tick();
#endif
            ChangeTimerIfRequested();
            ChartImporter.OnUpdate();
        }

        [Conditional("DEBUG")]
        private void ChangeTimerIfRequested()
        {
            var builtInTimeProviders = _builtInTimeProviders.Span;
            var selectedTimer = builtInTimeProviders[(int)_timer];
            if (MajTimeline.TimeProvider != selectedTimer)
            {
                MajDebug.LogWarning($"Time provider changed:\nOld:{MajTimeline.TimeProvider}\nNew:{selectedTimer}");
                MajTimeline.TimeProvider = selectedTimer;
            }
        }
        [Conditional("UNITY_EDITOR")]
        public void DebugImportChartArchive(string path)
        {
            ChartImporter.AddImportTask(path);
        }
        public void EnableGC()
        {
            GC.Collect();
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR //Android/iOS Only (GC Enable)
            UnityEngine.Scripting.GarbageCollector.GCMode = UnityEngine.Scripting.GarbageCollector.Mode.Enabled;
            MajDebug.LogWarning("GC has been enabled");
#endif
        }
        public void DisableGC()
        {
            GC.Collect();
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR //Android/iOS Only (GC Disable)
            UnityEngine.Scripting.GarbageCollector.GCMode = UnityEngine.Scripting.GarbageCollector.Mode.Disabled;
            MajDebug.LogWarning("GC has been disabled");
#endif
        }
        public static void RequestSave(object? sender)
        {
            try
            {
                if (OnSave is not null)
                {
                    OnSave(sender, null);
                }
            }
            catch (Exception ex)
            {
                MajDebug.LogException(ex);
            }
        }
        #region Events
        private void OnApplicationQuit()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            MajAndroidDisplayResolution.Restore();
#endif
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            RequestSave(this);
            try
            {
                if (OnAppQuit is not null)
                {
                    OnAppQuit(this, null);
                }
            }
            catch (Exception ex)
            {
                MajDebug.LogException(ex);
            }
            OnAppQuit = null;
            OnSave = null;
        }
        private void OnApplicationFocus(bool focus)
        {
            IsAppOnFocus = focus;
            if (OnAppFocus is not null)
            {
                OnAppFocus(this, focus);
            }
#if UNITY_ANDROID || UNITY_IOS
            if (!focus)
            {
                RequestSave(this);
            }
#endif
        }

        private void OnApplicationPause(bool pause)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!pause) MajAndroidDisplayResolution.RequestRefresh();
#endif
            if (OnAppPause is not null)
            {
                OnAppPause(this, pause);
            }
#if UNITY_ANDROID || UNITY_IOS
            if (pause)
            {
                RequestSave(this);
            }
#endif
        }

#if UNITY_IOS
        [Preserve]
        public void IOS_OnFileOpen(string tempFilePath)
        {
            ChartImporter.AddImportTask(tempFilePath);
        }
#endif
        #endregion
    }
}
