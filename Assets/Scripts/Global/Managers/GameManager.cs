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
using System.Security.Cryptography;
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
        private async UniTask CheckResourcesAsync()
        {

        }
        private async UniTask StartInternal()
        {
#if !UNITY_EDITOR
#if UNITY_IOS
            await UniTask.DelayFrame(2); // wait UnitySendMessage
#endif
#endif
            await UniTask.Yield();
            await MajInstances.SceneSwitcher.FadeInAsync();
            await CheckResourcesAsync();
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
            CabinetLed.Init();
            Recorder.Init();
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

    /// <summary>Extracts mobile packaged resources while preserving player-managed files and customizations.</summary>
    file static class MobileResourceUpdater
    {
        /// <summary>Identifies extraction commits whose chart and skin directory moves must be resumed.</summary>
        private const string PendingManagedAssetMovesMarkerName = ".pending-managed-asset-moves";

        /// <summary>The bundled chart collection relative to the extracted streaming assets.</summary>
        private const string BundledChartsRelativePath = "MaiCharts/Original";

        /// <summary>The bundled default skin relative to the extracted streaming assets.</summary>
        private const string BundledSkinRelativePath = "Skins/default";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeOrUpdate()
        {
            if (!MajEnv.AssetsDirectory.Exists)
            {
                if (ExtractAssets())
                {
                    var preservedExistingManagedAssets = MoveCharts();
                    preservedExistingManagedAssets |= MoveSkins();
                    ClearPendingManagedAssetMovesMarkerIfCompleted();
                    if (preservedExistingManagedAssets &&
                        ResourceManifestLoader.TryGetUpdateManifests(
                            out var existingManagedV1Hashes,
                            out var existingManagedV2Hashes,
                            out var existingManagedDiffPaths))
                    {
                        // AssetsPath can be missing while player-managed charts/skins still exist.
                        // Run the normal v1 hash gate for those existing files.
                        ApplyV2Diff(
                            existingManagedV1Hashes,
                            existingManagedV2Hashes,
                            existingManagedDiffPaths);
                    }
                }
                else
                {
                    MajDebug.LogError(
                        "Initial resource extraction was incomplete; managed assets were not moved.");
                }
                return;
            }

            CompletePendingManagedAssetMoves();
            if (!ResourceManifestLoader.TryGetUpdateManifests(
                    out var v1Hashes,
                    out var v2Hashes,
                    out var diffPaths))
            {
                return;
            }

            ApplyV2Diff(v1Hashes, v2Hashes, diffPaths);
            SyncMissingAssets(v2Hashes, diffPaths);
        }

        /// <summary>Replaces eligible official v1 files with verified packaged v2 bytes.</summary>
        /// <param name="v1Hashes">The previous official hashes used to protect customizations.</param>
        /// <param name="v2Hashes">The current hashes used to verify packaged replacements.</param>
        /// <param name="diffPaths">The official paths changed by this resource update.</param>
        /// <exception cref="UnauthorizedAccessException">A local destination cannot be queried.</exception>
        /// <exception cref="IOException">A local destination cannot be queried.</exception>
        private static void ApplyV2Diff(
            IReadOnlyDictionary<string, string> v1Hashes,
            IReadOnlyDictionary<string, string> v2Hashes,
            IReadOnlyCollection<string> diffPaths)
        {
            var updatedCount = 0;
            var preservedCount = 0;
            foreach (var relativePath in diffPaths)
            {
                var v2Hash = v2Hashes[relativePath];
                var destinationRoot = GetDestinationRoot(relativePath);
                var destinationFile = destinationRoot.FindFileAt(relativePath);
                var isRootManagedAsset = IsRootManagedAsset(relativePath);
                if (v1Hashes.TryGetValue(relativePath, out var v1Hash))
                {
                    if (destinationFile is null)
                    {
                        if (isRootManagedAsset)
                        {
                            MajDebug.LogInfo(
                                $"Resource update preserved player deletion: {relativePath}");
                            preservedCount++;
                            continue;
                        }
                    }
                    else if (!TryComputeFileSha256(destinationFile, out var localHash) ||
                             !string.Equals(localHash, v1Hash, StringComparison.OrdinalIgnoreCase))
                    {
                        MajDebug.LogInfo(
                            $"Resource update preserved player customization: {relativePath}");
                        preservedCount++;
                        continue;
                    }
                }
                else if (destinationFile is not null)
                {
                    // This path did not exist in v1. An existing local file is player-owned.
                    MajDebug.LogInfo(
                        $"Resource update preserved player file at new v2 path: {relativePath}");
                    preservedCount++;
                    continue;
                }
                else if (isRootManagedAsset)
                {
                    // Do not repopulate player-managed chart/skin trees during an upgrade.
                    MajDebug.LogInfo(
                        $"Resource update skipped new managed asset to preserve player layout: {relativePath}");
                    preservedCount++;
                    continue;
                }

                if (!TryReadPackagedResource(relativePath, out var v2Data))
                {
                    continue;
                }

                var packagedHash = ComputeSha256(v2Data);
                if (!string.Equals(packagedHash, v2Hash, StringComparison.OrdinalIgnoreCase))
                {
                    MajDebug.LogError(
                        $"Resource update skipped (packaged v2 hash mismatch): {relativePath}");
                    continue;
                }

                try
                {
                    WriteAllBytesAtomically(destinationRoot, relativePath, v2Data);
                    updatedCount++;
                    MajDebug.LogInfo($"Resource updated from official v1 to v2: {relativePath}");
                }
                catch (Exception exception)
                {
                    MajDebug.LogError(
                        $"Resource update failed while writing: {relativePath}\n{exception}");
                }
            }

            MajDebug.LogInfo(
                $"Mobile resource update finished: {updatedCount} updated, " +
                $"{preservedCount} customized or missing file(s) preserved.");
        }

        /// <summary>Stages and verifies every packaged resource before committing the extracted resource root.</summary>
        /// <returns>Whether all resources were verified and their staged directory was committed.</returns>
        private static bool ExtractAssets()
        {
            if (!ResourceManifestLoader.TryGetV2Hashes(out var v2Hashes))
            {
                return false;
            }

            // The extracted streaming assets are app-private native storage, so a staging sibling can be renamed into place atomically.
            var extractionRoot = MajEnv.AssetsDirectory.Location.TrimEnd(
                                     Path.DirectorySeparatorChar,
                                     Path.AltDirectorySeparatorChar) + ".extracting-v2";
            StorageDirectory stagingDirectory;
            try
            {
                if (FileSystem.Directory.Open(extractionRoot).Exists)
                {
                    FileSystem.Directory.Open(extractionRoot).Delete(recursive: true);
                }
                else if (FileSystem.File.Open(extractionRoot).Exists)
                {
                    FileSystem.File.Open(extractionRoot).Delete();
                }

                stagingDirectory = FileSystem.Directory.Create(extractionRoot);
            }
            catch (Exception exception)
            {
                MajDebug.LogError($"Failed to prepare resource extraction directory:\n{exception}");
                return false;
            }

            var succeeded = true;
            foreach (var (relativePath, hash) in v2Hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!CopyPackagedResource(relativePath, stagingDirectory, hash, "Extract"))
                {
                    succeeded = false;
                }
            }

            if (!succeeded)
            {
                return false;
            }

            try
            {
                stagingDirectory.CreateFile(PendingManagedAssetMovesMarkerName);
                FileSystem.Directory.Move(stagingDirectory.Location, MajEnv.AssetsDirectory.Location);
                return true;
            }
            catch (Exception exception)
            {
                MajDebug.LogError($"Failed to finish initial resource extraction:\n{exception}");
                return false;
            }
        }

        /// <summary>Copies missing unmanaged resources that are outside the explicit update diff.</summary>
        /// <param name="v2Hashes">The validated current packaged resource hashes.</param>
        /// <param name="diffPaths">The changed paths governed by the official v1 update gate.</param>
        /// <exception cref="UnauthorizedAccessException">A local destination cannot be queried.</exception>
        /// <exception cref="IOException">A local destination cannot be queried.</exception>
        private static void SyncMissingAssets(
            IReadOnlyDictionary<string, string> v2Hashes,
            IReadOnlyCollection<string> diffPaths)
        {
            foreach (var (relativePath, hash) in v2Hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (IsRootManagedAsset(relativePath) || diffPaths.Contains(relativePath))
                {
                    continue;
                }

                if (MajEnv.AssetsDirectory.FindFileAt(relativePath) is null)
                {
                    CopyPackagedResource(relativePath, MajEnv.AssetsDirectory, hash, "Sync missing");
                }
            }
        }

        /// <summary>Moves bundled charts from extraction into the player-managed chart root.</summary>
        /// <returns>Whether an existing player-managed destination was preserved.</returns>
        private static bool MoveCharts()
        {
            return MoveExtractedDirectory(BundledChartsRelativePath, MajEnv.ChartDirectory, "Original");
        }

        /// <summary>Moves the bundled skin from extraction into the player-managed skin root.</summary>
        /// <returns>Whether an existing player-managed destination was preserved.</returns>
        private static bool MoveSkins()
        {
            return MoveExtractedDirectory(BundledSkinRelativePath, MajEnv.SkinDirectory, "default");
        }

        /// <summary>Moves one extracted managed directory without replacing an existing player-owned destination.</summary>
        /// <param name="sourceRelativePath">The extracted directory relative to the extracted streaming assets.</param>
        /// <param name="destinationParent">The existing player-managed root receiving the directory.</param>
        /// <param name="destinationName">The directory name inside the player-managed root.</param>
        /// <returns>Whether an existing destination was preserved and the extracted duplicate removed.</returns>
        /// <remarks>
        /// The move is one native operation while the extracted assets and the player-managed root share a backend. A root
        /// on another backend, such as an Android SAF tree, receives a copy of the tree before the extracted source is
        /// deleted, so a failed transfer keeps the source and the pending marker for the next start.
        /// </remarks>
        /// <exception cref="UnauthorizedAccessException">The extracted source cannot be queried.</exception>
        /// <exception cref="IOException">The extracted source cannot be queried.</exception>
        private static bool MoveExtractedDirectory(string sourceRelativePath, StorageDirectory destinationParent, string destinationName)
        {
            var source = MajEnv.AssetsDirectory.FindDirectoryAt(sourceRelativePath);
            if (source is null)
            {
                MajDebug.LogError($"Move failed: source not found: {sourceRelativePath}");
                return false;
            }

            try
            {
                if (destinationParent.FindEntry(destinationName) is not null)
                {
                    // Never replace a pre-existing player-managed directory during extraction.
                    source.Delete(recursive: true);
                    MajDebug.LogInfo(
                        $"Preserved existing player-managed path during extraction: {destinationName} in {destinationParent.Location}");
                    return true;
                }

                var moved = StorageDirectoryTransfer.Move(source, destinationParent, destinationName);
                MajDebug.LogInfo($"Moved: {source.Location} -> {moved.Location}");
                return false;
            }
            catch (Exception exception)
            {
                MajDebug.LogError($"Move failed: {source.Location} -> {destinationName} in {destinationParent.Location}\n{exception}");
                return false;
            }
        }

        /// <summary>Resumes pending chart and skin moves recorded by an earlier extraction commit.</summary>
        /// <exception cref="UnauthorizedAccessException">The marker or extracted sources cannot be queried.</exception>
        /// <exception cref="IOException">The marker or extracted sources cannot be queried.</exception>
        private static void CompletePendingManagedAssetMoves()
        {
            if (MajEnv.AssetsDirectory.FindFile(PendingManagedAssetMovesMarkerName) is null)
            {
                return;
            }

            if (MajEnv.AssetsDirectory.FindDirectoryAt(BundledChartsRelativePath) is not null)
            {
                MoveCharts();
            }

            if (MajEnv.AssetsDirectory.FindDirectoryAt(BundledSkinRelativePath) is not null)
            {
                MoveSkins();
            }

            ClearPendingManagedAssetMovesMarkerIfCompleted();
        }

        /// <summary>Removes the pending marker after both extracted managed source directories are gone.</summary>
        /// <exception cref="UnauthorizedAccessException">An extracted source cannot be queried.</exception>
        /// <exception cref="IOException">An extracted source cannot be queried.</exception>
        private static void ClearPendingManagedAssetMovesMarkerIfCompleted()
        {
            if (MajEnv.AssetsDirectory.FindDirectoryAt(BundledChartsRelativePath) is not null ||
                MajEnv.AssetsDirectory.FindDirectoryAt(BundledSkinRelativePath) is not null)
            {
                return;
            }

            try
            {
                MajEnv.AssetsDirectory.FindFile(PendingManagedAssetMovesMarkerName)?.Delete();
            }
            catch (Exception exception)
            {
                MajDebug.LogError(
                    $"Failed to clean pending managed asset move marker: {PendingManagedAssetMovesMarkerName}\n{exception}");
            }
        }

        /// <summary>Verifies packaged bytes and commits one extracted or missing resource atomically below a destination root.</summary>
        /// <param name="relativePath">The portable path inside packaged resources, reused below the destination root.</param>
        /// <param name="destinationRoot">The existing directory receiving the verified bytes at the same relative path.</param>
        /// <param name="expectedHash">The official SHA-256 hash expected for the packaged bytes.</param>
        /// <param name="operation">The operation label used in resource logs.</param>
        /// <returns>Whether the packaged bytes were verified and committed.</returns>
        private static bool CopyPackagedResource(
            string relativePath,
            StorageDirectory destinationRoot,
            string expectedHash,
            string operation)
        {
            if (!TryReadPackagedResource(relativePath, out var data))
            {
                return false;
            }

            if (!string.Equals(ComputeSha256(data), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                MajDebug.LogError($"{operation} failed (packaged hash mismatch): {relativePath}");
                return false;
            }

            try
            {
                WriteAllBytesAtomically(destinationRoot, relativePath, data);
                MajDebug.LogInfo($"{operation}: {relativePath} -> {destinationRoot.Location}");
                return true;
            }
            catch (Exception exception)
            {
                MajDebug.LogError($"{operation} failed: {relativePath}\n{exception}");
                return false;
            }
        }

        /// <summary>Stages resource bytes beside the destination before a move or replacement inside the destination's backend.</summary>
        /// <param name="destinationRoot">The existing directory below which the resource is committed.</param>
        /// <param name="relativePath">The resource path below the root; missing directories are created.</param>
        /// <param name="data">The complete verified bytes to commit.</param>
        /// <exception cref="ArgumentException">The relative path is rooted, traverses upwards, or names no file.</exception>
        /// <exception cref="UnauthorizedAccessException">Creating, writing, or committing the file was denied.</exception>
        /// <exception cref="IOException">Staging or committing the file failed.</exception>
        /// <exception cref="NotSupportedException">The backend does not support creation or replacement.</exception>
        private static void WriteAllBytesAtomically(StorageDirectory destinationRoot, string relativePath, byte[] data)
        {
            var names = FileSystem.Path.Split(relativePath);
            if (names.Length == 0)
            {
                throw new ArgumentException("A resource path ending with a file name is required.", nameof(relativePath));
            }
            var parent = destinationRoot.CreateDirectoryAt(names.AsSpan(0, names.Length - 1));
            var name = names[names.Length - 1];
            var temporaryName = $"{name}.{Guid.NewGuid():N}.tmp";
            var temporaryFile = parent.CreateFile(temporaryName);
            try
            {
                temporaryFile.WriteAllBytes(data);
                var destinationFile = parent.FindFile(name);
                if (destinationFile is not null)
                {
                    FileSystem.File.Replace(temporaryFile.Location, destinationFile.Location);
                }
                else
                {
                    FileSystem.File.Move(temporaryFile.Location, parent.Location, name);
                }
            }
            finally
            {
                var leftover = parent.FindFile(temporaryName);
                if (leftover is not null)
                {
                    try
                    {
                        leftover.Delete();
                    }
                    catch (Exception exception)
                    {
                        MajDebug.LogError(
                            $"Failed to clean temporary resource file: {leftover.Location}\n{exception}");
                    }
                }
            }
        }

        /// <summary>Identifies chart and skin paths owned by the player-managed root.</summary>
        /// <param name="path">The portable manifest-relative path.</param>
        /// <returns>Whether the path belongs to a chart or skin tree.</returns>
        private static bool IsRootManagedAsset(string path)
        {
            return path.StartsWith("MaiCharts/", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("Skins/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Maps a portable manifest path to the resource root or player-managed root below which it lives.</summary>
        /// <param name="relativePath">The validated portable manifest path.</param>
        /// <returns>The root directory whose backend resolves the manifest path.</returns>
        private static StorageDirectory GetDestinationRoot(string relativePath)
        {
            return IsRootManagedAsset(relativePath) ? MajEnv.RootDirectory : MajEnv.AssetsDirectory;
        }

        /// <summary>Reads packaged bytes through local iOS storage or an Android APK archive entry.</summary>
        /// <param name="relativePath">The portable packaged resource path.</param>
        /// <param name="data">Receives the packaged bytes, or an empty array after failure.</param>
        /// <returns>Whether the packaged bytes were read successfully.</returns>
        private static bool TryReadPackagedResource(string relativePath, out byte[] data)
        {
            try
            {
                data = FileSystem.File.Open(GetPackagedResourceLocation(relativePath)).ReadAllBytes();
                return true;
            }
            catch (Exception exception)
            {
                MajDebug.LogError(
                    $"Resource update failed while reading packaged resource: " +
                    $"{relativePath}\n{exception}");
                data = Array.Empty<byte>();
                return false;
            }
        }

        /// <summary>Resolves one validated manifest path against the platform's packaged resource root.</summary>
        /// <param name="relativePath">The portable packaged resource path whose segments contain literal file names.</param>
        /// <returns>A local path or a jar URI with each archive entry segment escaped exactly once.</returns>
        /// <remarks>The packaged root is a Unity application path, so it is combined directly rather than through a handle.</remarks>
        /// <exception cref="ArgumentException">The local path is invalid.</exception>
        /// <exception cref="UriFormatException">An archive entry segment cannot be escaped.</exception>
        private static string GetPackagedResourceLocation(string relativePath)
        {
#if UNITY_ANDROID
            if (Application.streamingAssetsPath.StartsWith("jar:", StringComparison.OrdinalIgnoreCase))
            {
                var segments = relativePath.Split('/');
                for (var i = 0; i < segments.Length; i++)
                {
                    segments[i] = Uri.EscapeDataString(segments[i]);
                }
                return Application.streamingAssetsPath.TrimEnd('/') + "/" + string.Join("/", segments);
            }
#endif
            return Path.Combine(
                Application.streamingAssetsPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>Hashes a resource through its storage stream without buffering the whole file.</summary>
        /// <param name="file">The existing resource file to hash.</param>
        /// <param name="hash">Receives the lowercase SHA-256 hash, or an empty string after failure.</param>
        /// <returns>Whether the resource was successfully opened and hashed.</returns>
        private static bool TryComputeFileSha256(StorageFile file, out string hash)
        {
            try
            {
                using var stream = file.OpenRead();
                using var sha256 = SHA256.Create();
                hash = ToHexString(sha256.ComputeHash(stream));
                return true;
            }
            catch (Exception exception)
            {
                MajDebug.LogError($"Failed to hash local resource: {file.Location}\n{exception}");
                hash = string.Empty;
                return false;
            }
        }

        /// <summary>Hashes already buffered packaged resource bytes.</summary>
        /// <param name="data">The bytes to hash.</param>
        /// <returns>The lowercase SHA-256 hash.</returns>
        private static string ComputeSha256(byte[] data)
        {
            using var sha256 = SHA256.Create();
            return ToHexString(sha256.ComputeHash(data));
        }

        /// <summary>Formats hash bytes for comparison with validated resource manifest entries.</summary>
        /// <param name="hash">The hash bytes to format.</param>
        /// <returns>The lowercase hexadecimal hash without separators.</returns>
        private static string ToHexString(byte[] hash)
        {
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
