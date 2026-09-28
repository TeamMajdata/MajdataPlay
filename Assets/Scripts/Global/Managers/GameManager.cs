using AOT;
using Cysharp.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.i18n;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Test;
using MajdataPlay.Settings;
using MajdataPlay.Timer;
using ShimSkiaSharp;
using MajdataPlay.Syscall.Win32;
#if UNITY_STANDALONE_OSX
using MajdataPlay.Platform.MacOS;
#elif UNITY_IOS
using MajdataPlay.Platform.iOS;
#elif UNITY_ANDROID
using MajdataPlay.Platform.Android;
using MajdataPlay.Platform.Android.Runtime;
#endif
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using MajdataPlay.Diagnostics;
using MajdataPlay.Databases; // DO NOT REMOVE IT !!!

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

        [SerializeField] BuiltInTimeProvider _timer = BuiltInTimeProvider.Winapi;

        [SerializeField] bool _isEnterView = false;
        [SerializeField] bool _isEnterTest = false;

        readonly static List<IntPtr> _windowHandles = new();
        readonly static ReadOnlyMemory<ITimeProvider> _builtInTimeProviders = MajTimeline.BuiltInTimeProviders;

        protected override void Awake()
        {
            base.Awake();
            Majdata<GameManager>.SetAsSingleton(this);
            GameRuntime.Init();
        }
        void Start()
        {
            StartInternal().Forget();
        }
        async UniTask StartInternal()
        {
            await UniTask.CompletedTask;            
#if UNITY_ANDROID && !UNITY_EDITOR // Android Only (Sdk Version Log)
            MajDebug.LogInfo($"AndroidSdkVersion: {MajEnv.AndroidSdkVersion}");
            MajDebug.LogInfo($"TargetSdkVersion: {MajEnv.TargetSdkVersion}");
            using var packageManager = AndroidRuntime.CurrentActivity.Call<AndroidJavaObject>("getPackageManager");
            var packageName = AndroidRuntime.CurrentActivity.Call<string>("getPackageName");
            using var packageInfo = packageManager.Call<AndroidJavaObject>("getPackageInfo", packageName, 0);
            var androidVersionCode = 0L;
            if(MajEnv.AndroidSdkVersion >= 28)
            {
                androidVersionCode = packageInfo.Call<long>("getLongVersionCode");
            }
            else
            {
                androidVersionCode = packageInfo.Get<int>("versionCode");
            }
            MajDebug.LogInfo($"AndroidVerCode: {androidVersionCode}");
#endif
#if !UNITY_EDITOR
#if UNITY_ANDROID
            var intent = AndroidRuntime.CurrentActivity.Call<AndroidJavaObject>("getIntent");
            ChartImporter.Android_OnNewIntent(this, intent);
            AndroidRuntime.OnNewIntent += ChartImporter.Android_OnNewIntent;
#elif UNITY_IOS
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
                if(string.IsNullOrEmpty(Settings.Display.Language))
                {
                    if(Localization.SetLangByCode(Application.systemLanguage.ToLocale()))
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

        void DetectHWEncoder()
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
        static bool EnumWindowsCallback(IntPtr hWnd, int lParam)
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
        void EnterTestMode()
        {
            IOListener.NextScene = "Title";
#if UNITY_STANDALONE_WIN && ENABLE_MONO
            MajEnv.GameProcess.PriorityClass = ProcessPriorityClass.AboveNormal;
#endif
            SceneManager.LoadScene("Test");
        }

        void EnterTitle()
        {
#if UNITY_STANDALONE_WIN && ENABLE_MONO
            MajEnv.GameProcess.PriorityClass = ProcessPriorityClass.AboveNormal;
#endif
            SceneManager.LoadScene("Title");
        }

        void EnterView()
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
#if UNITY_ANDROID && !UNITY_EDITOR
            MajDebug.LogInfo($"Oniimai Android FPS selected: target={Application.targetFrameRate}, setting={Settings.Display.FPSLimit}");
#endif
        }

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        async UniTask ApplyMacOSFullscreenAsync()
        {
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            NativePresentation.EnterNativeFullscreenIfNeeded();
        }
#endif

        void Update()
        {
            ChangeTimerIfRequested();
            ChartImporter.OnUpdate();
        }

        [Conditional("DEBUG")]
        void ChangeTimerIfRequested()
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
            GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
            MajDebug.LogWarning("GC has been enabled");
#endif
        }
        public void DisableGC()
        {
            GC.Collect();
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR //Android/iOS Only (GC Disable)
            GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
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
        void OnApplicationQuit()
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
        void OnApplicationFocus(bool focus)
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

        void OnApplicationPause(bool pause)
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
        
        static class ChartImporter
        {
            static string _importRoot = string.Empty;
            static ValueTask _importTask = UniTask.CompletedTask;
            static readonly ConcurrentQueue<string> _pendingImportTasks = new ConcurrentQueue<string>();
            
            public static ValueTask OnStartAsync()
            {
                _importRoot = Path.Combine(MajEnv.ChartPath, "Import");
                if (_pendingImportTasks.Count != 0)
                {
                    _importTask = Import();
                    return _importTask;
                }
                return UniTask.CompletedTask;
            }
            public static void AddImportTask(string tempFilePath)
            {
                _pendingImportTasks.Enqueue(tempFilePath);
            }
            public static void OnUpdate()
            {
                if (SceneSwitcher.CurrentScene == MajScenes.Empty || 
                    _pendingImportTasks.Count == 0 ||
                    !_importTask.IsCompleted)
                {
                    return;
                }
                _importTask = Import();
            }
            static async ValueTask Import()
            {
                static void CopyDirectory(string sourceDir, string destDir)
                {
                    var dirs = new Stack<(string src, string dst)>();
                    dirs.Push((sourceDir, destDir));

                    while (dirs.Count > 0)
                    {
                        var (currentSource, currentDest) = dirs.Pop();

                        Directory.CreateDirectory(currentDest);

                        foreach (var file in Directory.GetFiles(currentSource))
                        {
                            var destFile = Path.Combine(currentDest, Path.GetFileName(file));
                            File.Copy(file, destFile, true);
                        }

                        foreach (var subDir in Directory.GetDirectories(currentSource))
                        {
                            var newDest = Path.Combine(currentDest, Path.GetFileName(subDir));
                            dirs.Push((subDir, newDest));
                        }
                    }
                }
                var nextScene = (MajScenes?)MajScenes.List;
                if(SceneSwitcher.CurrentScene == MajScenes.Init)
                {
                    await MajInstances.SceneSwitcher.FadeInAsync();
                    nextScene = null;
                }
                else
                {
                    while (SceneSwitcher.CurrentScene == MajScenes.Title)
                    {
                        nextScene = MajScenes.Login;
                        await UniTask.Yield();
                    }
                    await MajInstances.SceneSwitcher.SwitchSceneAsync("Empty", false);
                    await UniTask.Delay(300);
                }
                Directory.CreateDirectory(_importRoot);
                while (_pendingImportTasks.TryDequeue(out var tempFilePath))
                {
                    MajDebug.LogDebug("[ZipImporter] Got file: " + tempFilePath);

                    if (!File.Exists(tempFilePath))
                    {
                        MajDebug.LogError("[ZipImporter] File not found: " + tempFilePath);
                        continue;
                    }

                    var ext = Path.GetExtension(tempFilePath).ToLowerInvariant();
                    if (ext != ".zip" && ext != ".adx")
                    {
                        MajDebug.LogError("[ZipImporter] Unsupported extension: " + ext);
                        continue;
                    }
                    var tempOutDir = Path.Combine(MajEnv.TempPath, Guid.NewGuid().ToString());
                    
                    try
                    {
                        var dirInfo = Directory.CreateDirectory(tempOutDir);
                        using var archive = ZipFile.OpenRead(tempFilePath);
                        var totalSize = archive.Entries.Sum(x => x.Length);
                        var decompressedSize = 0L;
                        var destRootFull = Path.GetFullPath(tempOutDir);
                        if (!destRootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                        {
                            destRootFull += Path.DirectorySeparatorChar;
                        }
                        foreach (var entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name))
                            {
                                continue;
                            }

                            var combinedPath = Path.Combine(tempOutDir, entry.FullName);
                            var fullPath = Path.GetFullPath(combinedPath);

                            if (!fullPath.StartsWith(destRootFull, StringComparison.Ordinal))
                            {
                                throw new IOException("Zip entry escapes destination: " + entry.FullName);
                            }

                            var dir = Path.GetDirectoryName(fullPath);
                            if (!string.IsNullOrEmpty(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }

                            if (File.Exists(fullPath))
                            {
                                File.Delete(fullPath);
                            }

                            entry.ExtractToFile(fullPath);
                            decompressedSize += entry.Length;
                            MajInstances.SceneSwitcher.SetLoadingText($"Extracting...\n{decompressedSize * 100 / (double)totalSize:F2}%");
                            await UniTask.Yield();
                        }
                        var subDirs = dirInfo.GetDirectories();
                        if (subDirs.Length == 0)
                        {
                            continue;
                        }
                        Parallel.For(0, subDirs.Length, j =>
                        {
                            var dir = subDirs[j];
                            if (dir.Attributes.HasFlag(FileAttributes.Hidden) || dir.Attributes.HasFlag(FileAttributes.System) || dir.Name.StartsWith("."))
                            {
                                return;
                            }
                            var subDirCount = dir.EnumerateDirectories()
                                                 .Count(x => !(x.Attributes.HasFlag(FileAttributes.Hidden) || x.Attributes.HasFlag(FileAttributes.System) || x.Name.StartsWith(".")));

                            if (subDirCount == 0)
                            {
                                var dirName = dir.Name;
                                var dstPath = Path.Combine(_importRoot, dirName);
                                for (var i = 0; Directory.Exists(dstPath); i++)
                                {
                                    dstPath = Path.Combine(_importRoot, $"{dirName} ({i + 1})");
                                }
                                CopyDirectory(dir.FullName, dstPath);
                            }
                            else
                            {
                                var dirName = dir.Name;
                                var dstPath = Path.Combine(MajEnv.ChartPath, dirName);
                                for (var i = 0; Directory.Exists(dstPath); i++)
                                {
                                    dstPath = Path.Combine(MajEnv.ChartPath, $"{dirName} ({i + 1})");
                                }
                                CopyDirectory(dir.FullName, dstPath);
                            }
                            Directory.Delete(dir.FullName, true);
                        });
                    }
                    catch (Exception e)
                    {
                        MajDebug.LogError("[ZipImporter] Extract FAILED: " + e);

                        try { Directory.Delete(tempOutDir, true); } catch { /* ignore */ }
                        return;
                    }
                    finally
                    {
#if UNITY_ANDROID
                        File.Delete(tempFilePath);
#endif
                    }
                }
                var progress = new Progress<string>();
                progress.ProgressChanged += (o, e) =>
                {
                    MajInstances.SceneSwitcher.SetLoadingText(e);
                };
                var task = SongStorage.RefreshLocalAsync(progress);
                while (!task.IsCompleted)
                {
                    await UniTask.Yield();
                }
                if (!task.IsCompletedSuccessfully)
                {
                    MajInstances.SceneSwitcher.SetLoadingText("MAJTEXT_ERR_SCAN_CHARTS_FAILED".i18n(), Color.red);
                }
                else
                {
                    MajInstances.SceneSwitcher.SetLoadingText(string.Empty);
                }
                await UniTask.Delay(3000);
                if(nextScene is MajScenes scene)
                {
                    await MajInstances.SceneSwitcher.SwitchSceneAsync(scene.ToString());
                }
            }

            public static void Android_OnNewIntent(object? sender, AndroidJavaObject? intent)
            {
                var intentUri = intent?.Call<AndroidJavaObject>("getData");
                if (intentUri is null)
                {
                    return;
                }
                using var fileIOKit = new AndroidJavaClass("net.majdata.majdataplay.FileIOKit");
                var tempPath = Path.Combine(MajEnv.CachePath, "Runtime", $"{Guid.NewGuid()}.zip");
                var returnCode = fileIOKit.CallStatic<int>("CopyContentToFile", intentUri, tempPath);

                MajDebug.LogDebug($"[Android][FileIOKit] return {returnCode}");
                if(returnCode != 0)
                {
                    return;
                }

                AddImportTask(tempPath);
            }
        }
    }
}
