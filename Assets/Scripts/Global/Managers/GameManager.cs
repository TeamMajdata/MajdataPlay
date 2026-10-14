using AOT;
using Cysharp.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.i18n;
using MajdataPlay.IO;
using MajdataPlay.IO.Storage;
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
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using MajdataPlay.Diagnostics;
using MajdataPlay.Databases;
using MajdataPlay.Import; // DO NOT REMOVE IT !!!

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

        [SerializeField] private BuiltInTimeProvider _timer = BuiltInTimeProvider.Winapi;

        [SerializeField] private bool _isEnterView = false;
        [SerializeField] private bool _isEnterTest = false;

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
        private async UniTask StartInternal()
        {
            await UniTask.CompletedTask;
#if !UNITY_EDITOR
#if UNITY_IOS
            await UniTask.DelayFrame(2); // wait UnitySendMessage
#endif
#endif
            await ChartImporter.OnStartAsync();
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            DesktopResourceRestorer.RestoreManagedAssetsIfMissing();
#endif
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
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
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
#if !(UNITY_ANDROID || UNITY_IOS)
            QualitySettings.vSyncCount = Settings.Display.VSync ? 1 : 0;
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
            CabinetLed.Init();
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
            await MajInstances.SceneSwitcher.FadeOutAsync();
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
            SceneManager.LoadScene("Test");
        }

        private void EnterTitle()
        {
#if UNITY_STANDALONE_WIN && ENABLE_MONO
            MajEnv.GameProcess.PriorityClass = ProcessPriorityClass.AboveNormal;
#endif
            SceneManager.LoadScene("Title");
        }

        private void EnterView()
        {
#if UNITY_STANDALONE_WIN && ENABLE_MONO
            MajEnv.GameProcess.PriorityClass = ProcessPriorityClass.BelowNormal;
#endif
            SceneManager.LoadScene("View");
        }
        private void ApplyDisplaySettings()
        {
            var displayOptions = MajEnv.Settings.Display;
            QualitySettings.SetQualityLevel((int)displayOptions.RenderQuality, true);
#if UNITY_STANDALONE
            QualitySettings.vSyncCount = displayOptions.VSync ? 1 : 0;
#endif
            Application.targetFrameRate = displayOptions.FPSLimit;
            ApplyRenderScale(displayOptions.RenderScale);
        }

        internal static void ApplyRenderScale(int percentage)
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)
            {
                pipeline.renderScale = Mathf.Clamp(percentage, 50, 100) / 100f;
            }
        }

        private void ApplyScreenConfig()
        {
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
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
            Application.targetFrameRate = Settings.Display.FPSLimit;
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
