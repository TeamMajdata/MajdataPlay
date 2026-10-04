using Cysharp.Threading.Tasks;
#if UNITY_STANDALONE
using HidSharp.Platform.Windows;
using HidSharp.Platform.MacOS;
#endif
using MajdataPlay.Buffers;
using MajdataPlay.Collections;
using MajdataPlay.Extensions;
using MajdataPlay.Net;
using MajdataPlay.Numerics;
using MajdataPlay.Settings;
using MajdataPlay.Settings.Runtime;
using MajdataPlay.Utils;
#if UNITY_ANDROID
using MajdataPlay.Platform.Android;
#elif UNITY_IOS
using MajdataPlay.Platform.iOS;
#endif
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Scripting;
using MajdataPlay.Net.Handlers;
using Random = System.Random;
using UnityEngine.Networking;
using MajdataPlay.Diagnostics;
using SkiaSharp;
using System.Text;
using MajdataPlay.Net.Curl;

#nullable enable
namespace MajdataPlay
{
    internal static class MajEnv
    {
        public const int DEFAULT_LAYER = 0;
        public const int HIDDEN_LAYER = 3;
        public const int UI_LAYER = 5;
        public const int HTTP_BUFFER_SIZE = 8192;
        public const int HTTP_REQUEST_MAX_RETRY = 4;
        public const int HTTP_TIMEOUT_MS = 10000;
        public const float FRAME_LENGTH_SEC = 1f / 60;
        public const float FRAME_LENGTH_MSEC = FRAME_LENGTH_SEC * 1000;

        public const int IO_DEVICE_RECONNECT_INTERVAL_MSEC = 2000;
        public const int IO_DEVICE_RECONNECT_MAX_RETRIES = 5;

        public const int ONLINE_RESPONSE_CACHE_TTL_SEC = 30 * 60;

        public static readonly System.Threading.ThreadPriority THREAD_PRIORITY_IO =
            System.Threading.ThreadPriority.AboveNormal;

        public static readonly System.Threading.ThreadPriority THREAD_PRIORITY_MAIN =
            System.Threading.ThreadPriority.Normal;

        public static readonly string[] SUPPORTED_VIDEO_FORMAT = new string[2]
        {
            ".webm",
            ".mp4"
        };

#if UNITY_ANDROID // Android Only (User Agent)
        public static string HTTP_USER_AGENT { get; } = $"MajdataPlay Android/{MajInstances.GameVersion.ToString()}";
#elif UNITY_IOS // iOS Only (User Agent)
        public static string HTTP_USER_AGENT { get; } = $"MajdataPlay iOS/{MajInstances.GameVersion.ToString()}";
#else
        public static string HTTP_USER_AGENT { get; } = $"MajdataPlay/{MajInstances.GameVersion.ToString()}";
#endif
        internal static HardwareEncoder HWEncoder { get; set; } = HardwareEncoder.None;
        internal static RunningMode Mode { get; set; } = RunningMode.Play;
#if UNITY_EDITOR
        public static bool IsEditor { get; } = true;
#else
        public static bool IsEditor { get; } = false;
#endif

#if UNITY_ANDROID // Android Only (Sdk Version Declare)
        public static int AndroidSdkVersion
        {
            get; 
            private set;
        }
        public static int TargetSdkVersion
        {
            get;
            private set;
        }
#endif

        public static string RootPath { get; private set; } = string.Empty;
        public static string AssetsPath { get; private set; } = string.Empty;
        public static string CachePath { get; private set; } = string.Empty;
        public static string TempPath { get; private set; } = string.Empty;
        public static string ChartPath { get; private set; } = string.Empty;
        public static string SettingsPath { get; private set; } = string.Empty;
        public static string SkinPath { get; private set; } = string.Empty;
        public static string LogsPath { get; private set; } = string.Empty;
        public static string LangPath { get; private set; } = string.Empty;
        public static string ScoreDBPath { get; private set; } = string.Empty;
        public static string LegacyScoreDBPath { get; private set; } = string.Empty;
        public static string FavoriteDBPath { get; private set; } = string.Empty;
        public static string LogPath { get; private set; } = string.Empty;
        public static string RecordOutputsPath { get; private set; } = string.Empty;
        public static bool IsLowMemoryDevice { get; private set; }
        public static MachineInfo MachineInfo { get; private set; } = new()
        {
            Name = "MajdataPlay Client",
            Description = "MajdataPlay Client QR Code Authentication",
        };
        public static Random Randomizer
        {
            get
            {
                if(s_randomizer is null)
                {
                    s_randomizer = new();
                }
                return s_randomizer;
            }
        }
        public static Thread MainThread { get; } = Thread.CurrentThread;
        public static Process GameProcess { get; } = Process.GetCurrentProcess();

        //readonly static HttpClientHandler _httpClientHandler = new HttpClientHandler()
        //{
        //    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        //    Proxy = WebRequest.GetSystemWebProxy(),
        //    UseProxy = true,
        //    UseCookies = true,
        //    CookieContainer = new CookieContainer(),
        //    //MaxConnectionsPerServer = 64,
        //};
        readonly static CurlHttpMessageHandler _httpClientHandler = new CurlHttpMessageHandler()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            Proxy = WebRequest.GetSystemWebProxy(),
            UseProxy = true,
            CookieContainer = new CookieContainer(),
            MaxConnectionsPerServer = 64,
        };
        readonly static UnityHttpMessageHandler _unityHttpHandler = new UnityHttpMessageHandler();

#if true
        public static HttpClient SharedHttpClient { get; } = new HttpClient(_httpClientHandler)
        {
            Timeout = TimeSpan.FromMilliseconds(HTTP_TIMEOUT_MS),
            DefaultRequestHeaders =
            {
                UserAgent = { new ProductInfoHeaderValue("MajdataPlay", MajInstances.GameVersion.ToString()) },
            }
        };
#else
        public static HttpClient SharedHttpClient { get; } = new HttpClient(_unityHttpHandler)
        {
            Timeout = TimeSpan.FromMilliseconds(HTTP_TIMEOUT_MS),
            DefaultRequestHeaders =
            {
                UserAgent = { new ProductInfoHeaderValue("MajdataPlay", MajInstances.GameVersion.ToString()) },
            }
        };
#endif

        public static GameSetting Settings { get; private set; }
        public static RuntimeConfig RuntimeConfig { get; private set; }
        public static ApiEndpoint[] ApiEndpoints { get; private set; }

        public static CancellationToken GlobalCT
        {
            get { return _globalCTS.Token; }
        }

        public static JsonSerializerSettings UserJsonReaderOption { get; } = new()
        {
            Formatting = Formatting.Indented,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Converters =
            {
                new StringEnumConverter()
            }
        };

        [ThreadStatic]
        static Random? s_randomizer;
        static string _runtimeConfigPath = string.Empty;
        readonly static CancellationTokenSource _globalCTS = new();


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ChangedSynchronizationContext()
        {
#if !UNITY_EDITOR
            SynchronizationContext.SetSynchronizationContext(new UniTaskSynchronizationContext());
#endif
        }
        #region Init core
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Init()
        {
            InitPath();
            CheckDirectories();
            InitLogger();
            InitSystemSettings();
            InitUserSettings();
            ReadiOSNativeSettings();
            InitNetwork();
            InitRuntimeConfig();
            InitOnlineEndpoints();
            InitOnlineMachineInfo();
            InitOthers();
        }

        static void InitPath()
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            RootPath = Path.Combine(Application.dataPath, "../");
            AssetsPath = Application.streamingAssetsPath;
            CachePath = Path.Combine(RootPath, "Cache");
#elif UNITY_ANDROID
            var versionClass = AndroidJNI.FindClass("android/os/Build$VERSION");
            var fieldID = AndroidJNI.GetStaticFieldID(versionClass, "SDK_INT", "I");
            AndroidSdkVersion = AndroidJNI.GetStaticIntField(versionClass, fieldID);
            var appInfo = AndroidRuntime.CurrentActivity.Call<AndroidJavaObject>("getApplicationInfo");
            TargetSdkVersion = appInfo.Get<int>("targetSdkVersion");

            if (AndroidSdkVersion < 30 || TargetSdkVersion < 30) // < Android 11
            {
                var androidStoragePermissions = new string[]
                {
                    Permission.ExternalStorageRead,
                    Permission.ExternalStorageWrite,
                };
                var isGranted = true;
                for (var i = 0; i < androidStoragePermissions.Length; i++)
                {
                    var flag = 0;
                    var permission = androidStoragePermissions[i];
                RECHECK_PERMISSION:
                    if (!Permission.HasUserAuthorizedPermission(permission))
                    {
                        switch (flag)
                        {
                            case 0:
                                Permission.RequestUserPermission(permission);
                                flag = 1;
                                goto RECHECK_PERMISSION;
                            case 1:
                                isGranted = false;
                                goto BREAK_LOOP;
                        }
                        continue;
                    BREAK_LOOP:
                        break;
                    }
                }
                if (isGranted)
                {
                    RootPath = "/sdcard/Documents/MajdataPlay";
                    if (!Directory.Exists(RootPath))
                    {
                        Directory.CreateDirectory(RootPath);
                    }
                    var noMediaFlag = new FileInfo(Path.Combine(RootPath, ".nomedia"));
                    if (!noMediaFlag.Exists)
                    {
                        try
                        {
                            noMediaFlag.Create().Dispose();
                            MajDebug.LogDebug("Created .nomedia flag file");
                        }
                        catch (Exception e)
                        {
                            MajDebug.LogError("Failed to create .nomedia flag file");
                            MajDebug.LogException(e);
                        }
                    }
                    else
                    {
                        MajDebug.LogDebug(".nomedia flag file exists");
                    }
                    AssetsPath = Path.Combine(RootPath, "ExtStreamingAssets/");
                }
                else
                {
                    RootPath = Application.persistentDataPath;
                    AssetsPath = Path.Combine(Application.persistentDataPath, "ExtStreamingAssets/");
                }
            }
            else // >= Android 11
            {
                RootPath = Application.persistentDataPath;
                AssetsPath = Path.Combine(Application.persistentDataPath, "ExtStreamingAssets/");
            }
                
            CachePath = Application.temporaryCachePath;
#elif UNITY_IOS
            RootPath = Application.persistentDataPath;
            AssetsPath = Path.Combine(Application.persistentDataPath, "ExtStreamingAssets/");
            CachePath = Application.temporaryCachePath;
#else
            throw new NotImplementedException();
#endif
            _runtimeConfigPath = Path.Combine(CachePath, "Runtime", "config.json");
            ChartPath = Path.Combine(RootPath, "MaiCharts");
            SettingsPath = Path.Combine(RootPath, "settings.json");
            SkinPath = Path.Combine(RootPath, "Skins");
            LogsPath = Path.Combine(RootPath, $"Logs");
            LangPath = Path.Combine(AssetsPath, "Langs");
            LegacyScoreDBPath = Path.Combine(RootPath,
                "MajDatabase.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db");
            ScoreDBPath = Path.Combine(RootPath, "MajScores.db");
            FavoriteDBPath = Path.Combine(CachePath, "Runtime", "MajFavorites.db");
            LogPath = Path.Combine(LogsPath, $"MajPlayRuntime.log");
            RecordOutputsPath = Path.Combine(RootPath, "RecordOutputs");
            TempPath = Path.Combine(CachePath, "Temp");
        }
        static void CheckDirectories()
        {
            var netCachePath = Path.Combine(CachePath, "Net");
            var runtimeCachePath = Path.Combine(CachePath, "Runtime");
            TempPath = Path.Combine(CachePath, "Temp");

            CreateDirectoryIfNotExists(SkinPath);
            CreateDirectoryIfNotExists(CachePath);
            CreateDirectoryIfNotExists(runtimeCachePath);
            CreateDirectoryIfNotExists(netCachePath);
            CreateDirectoryIfNotExists(TempPath);
            CreateDirectoryIfNotExists(ChartPath);
            CreateDirectoryIfNotExists(RecordOutputsPath);
            CreateDirectoryIfNotExists(LogsPath);
            CreateDirectoryIfNotExists(Path.Combine(RootPath, "Runtime"));
        }
        static void InitLogger()
        {
            var oldLogPath = LogPath + ".old";

            if (File.Exists(oldLogPath))
            {
                File.Delete(oldLogPath);
            }
            if (File.Exists(LogPath))
            {
                File.Move(LogPath, oldLogPath);
            }
            var fileWriter = new StreamWriter(LogPath, append: true, encoding: Encoding.UTF8);
            fileWriter.AutoFlush = true;

            MajDebug.SetLogWriter(fileWriter);
        }
        static void InitSystemSettings()
        {
            SharedHttpClient.Timeout = TimeSpan.FromMilliseconds(HTTP_TIMEOUT_MS);
            MainThread.Priority = THREAD_PRIORITY_MAIN;

            IsLowMemoryDevice = SystemInfo.systemMemorySize < 4096;

            UnityWebRequestFactory.Timeout = TimeSpan.FromMilliseconds(HTTP_TIMEOUT_MS);
            UnityWebRequestFactory.UserAgent = HTTP_USER_AGENT;

            GameManager.OnAppQuit += OnApplicationQuit;
            GameManager.OnSave += OnSave;

            var s = "\n";
            s += $"################ MajdataPlay Startup Check ################\n";
            s += $"     OS       : {SystemInfo.operatingSystem}\n";
            s += $"     Model    : {SystemInfo.deviceModel} - {SystemInfo.deviceType}\n";
            s += $"     Processor: {SystemInfo.processorType}\n";
            s += $"     Memory   : {SystemInfo.systemMemorySize} MB\n";
            s +=
                $"     Graphices: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB) - {SystemInfo.graphicsDeviceType}\n";
            s += $"################     Startup Check  End    ################";
            MajDebug.LogInfo(s);
            MajDebug.LogInfo($"PID: {MajEnv.GameProcess.Id}");
            MajDebug.LogInfo($"Version: {MajInstances.GameVersion}");

            // Check iOS Native Settings
#if UNITY_IOS 
            MajDebug.LogInfo($"""

                             ===== Majdata iOS Settings Debug =====
    
                             Inited:           {IOSNativeSettings.Inited}
                             AppVersion:       {IOSNativeSettings.AppVersion}

                             Online:           {IOSNativeSettings.Online}

                             DebugLogLevel:    {IOSNativeSettings.DebugLogLevel}
                             DebugNoCache:     {IOSNativeSettings.DebugNoCache}
                             DebugNoteFolding: {IOSNativeSettings.DebugNoteFolding}

                             MajnetEnabled:    {IOSNativeSettings.MajnetEnabled}
                             MajnetApi:        {IOSNativeSettings.MajnetApi}
                             MajnetUsername:   {(string.IsNullOrEmpty(IOSNativeSettings.MajnetUsername) ? "<empty>" : "***")}
                             MajnetPassword:   {(string.IsNullOrEmpty(IOSNativeSettings.MajnetPassword) ? "<empty>" : "***")}
                             MajnetAutoLogin:  {IOSNativeSettings.MajnetAutoLogin}

                             CustomEnabled:    {IOSNativeSettings.CustomEnabled}
                             CustomName:       {IOSNativeSettings.CustomName}
                             CustomApi:        {IOSNativeSettings.CustomApi}
                             CustomUsername:   {(string.IsNullOrEmpty(IOSNativeSettings.CustomUsername) ? "<empty>" : "***")}
                             CustomPassword:   {(string.IsNullOrEmpty(IOSNativeSettings.CustomPassword) ? "<empty>" : "***")}
                             CustomAutoLogin:  {IOSNativeSettings.CustomAutoLogin}

                             ===============================
                             """
                             );
#endif

#if !UNITY_EDITOR
            if(MainThread.Name is not null)
            {
                MainThread.Name = "MajdataPlay MainThread";
            }
#endif
        }
        static void InitUserSettings()
        {
            if (File.Exists(SettingsPath))
            {
                var js = File.ReadAllText(SettingsPath);
                GameSetting? setting;


                if (!Serializer.Json.TryDeserialize(js, out setting, out var e, UserJsonReaderOption) || setting is null)
                {
                    Settings = new();
                    MajDebug.LogError($"Failed to read setting from file\nException: {e}");
                    var bakFileName = $"{SettingsPath}.bak";
                    while (File.Exists(bakFileName))
                    {
                        bakFileName = $"{bakFileName}.bak";
                    }

                    try
                    {
                        File.Copy(SettingsPath, bakFileName, true);
                    }
                    catch
                    {
                    }
                }
                else
                {
                    Settings = setting;
                }
            }
            else
            {
                Settings = new GameSetting();

                var json = Serializer.Json.Serialize(Settings, UserJsonReaderOption);
                File.WriteAllText(SettingsPath, json);
            }

#if UNITY_STANDALONE
            Settings.IO.InputDevice.ButtonRing.PollingRateMs =
                Math.Max(0, Settings.IO.InputDevice.ButtonRing.PollingRateMs);
            Settings.IO.InputDevice.TouchPanel.PollingRateMs =
                Math.Max(0, Settings.IO.InputDevice.TouchPanel.PollingRateMs);
            Settings.IO.InputDevice.ButtonRing.DebounceThresholdMs =
                Math.Max(0, Settings.IO.InputDevice.ButtonRing.DebounceThresholdMs);
            Settings.IO.InputDevice.TouchPanel.DebounceThresholdMs =
                Math.Max(0, Settings.IO.InputDevice.TouchPanel.DebounceThresholdMs);
#endif
            Settings.Display.InnerJudgeDistance = Settings.Display.InnerJudgeDistance.Clamp(0, 1);
            Settings.Display.OuterJudgeDistance = Settings.Display.OuterJudgeDistance.Clamp(0, 1);

            MajDebug.MinLogLevel = Settings.Debug.DebugLevel;
        }
        static void ReadiOSNativeSettings()
        {
#if UNITY_IOS
            if (IOSNativeSettings.DebugNoCache)
            {
                TryDeleteDirectory(Path.Combine(CachePath, "Net"));
                TryDeleteDirectory(Path.Combine(CachePath, "View"));
                MajDebug.LogInfo("[MajEnv][Init]iOS setting 'no_cache' is enabled. Cleared cache folders: Net, View.");
            }
            else
            {
                MajDebug.LogInfo("[MajEnv][Init]iOS setting 'no_cache' is disabled.");
            }
            if (Enum.TryParse<LogLevel>(IOSNativeSettings.DebugLogLevel, false, out var logLevel))
            {
                Settings.Debug.DebugLevel = logLevel;
            }

            Settings.Debug.NoteFolding = IOSNativeSettings.DebugNoteFolding;
            Settings.Online.Enable = IOSNativeSettings.Online;

            MajDebug.MinLogLevel = Settings.Debug.DebugLevel;
#endif
        }
        static void InitNetwork()
        {
#if UNITY_STANDALONE
            if(Settings.Online.UseProxy)
            {
                if(!string.IsNullOrEmpty(Settings.Online.Proxy))
                {
                    if(!Uri.TryCreate(Settings.Online.Proxy, UriKind.Absolute, out var proxyUri))
                    {
                        MajDebug.LogError($"Invalid proxy URL: {Settings.Online.Proxy}");
                        return;
                    }
                    switch (proxyUri.Scheme)
                    {
                        case "http":
                        case "https":
                            break;
                        default:
                            MajDebug.LogError($"Unsupported proxy scheme: {proxyUri.Scheme}");
                            return;
                    }
                    var proxy = new WebProxy(proxyUri);
                    if(!string.IsNullOrEmpty(proxyUri.UserInfo))
                    {
                        var parts = proxyUri.UserInfo.Split(":", 2);
                        var username = parts[0];
                        var password = parts.Length > 1 ? parts[1] : string.Empty;

                        if(!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                        {
                            proxy.Credentials = new NetworkCredential(username, password);
                        }
                        else
                        {
                            MajDebug.LogWarning($"Invalid proxy credentials: {proxyUri.UserInfo}");
                        }
                    }
                    _httpClientHandler.Proxy = proxy;
                }
            }
            else
            {
                _httpClientHandler.UseProxy = false;
                //_httpClientHandler.Proxy = null;
            }
#endif
        }
        static void InitOnlineEndpoints()
        {
            UnityWebRequest.ClearCookieCache();
            using (var buffer = new PooledList<ApiEndpoint>())
            {
                using var apiEndpoints = new PooledList<ApiEndpoint>();
                apiEndpoints.AddRange(Settings.Online.ApiEndpoints.AsSpan());

#if UNITY_IOS && !UNITY_EDITOR
                if (IOSNativeSettings.MajnetEnabled)
                {
                    var apiUri = default(Uri);
                    var isValid = Uri.TryCreate(IOSNativeSettings.MajnetApi, UriKind.Absolute, out apiUri);

                    if (isValid)
                    {
                        var i = apiEndpoints.FindIndex(x => x.Url.OriginalString == IOSNativeSettings.MajnetApi);
                        if(i != -1)
                        {
                            apiEndpoints.RemoveAt(i);
                        }
                        apiEndpoints.Add(new ApiEndpoint()
                        {
                            Name = "MajdataNET",
                            Url = apiUri!,
                            Username = IOSNativeSettings.MajnetUsername,
                            Password = IOSNativeSettings.MajnetPassword,
                            AutoLogin = IOSNativeSettings.MajnetAutoLogin,
                        });
                    }
                    else
                    {
                        MajDebug.LogWarning($"[iOSNativeSettings]Invalid uri: {IOSNativeSettings.MajnetApi}, ignored.");
                    }
                }
                if (IOSNativeSettings.CustomEnabled)
                {
                    var apiUri = default(Uri);
                    var isValid = Uri.TryCreate(IOSNativeSettings.CustomApi, UriKind.Absolute, out apiUri);

                    if (isValid)
                    {
                        apiEndpoints.Add(new ApiEndpoint()
                        {
                            Name = IOSNativeSettings.CustomName ?? "Custom Api",
                            Url = apiUri!,
                            Username = IOSNativeSettings.CustomUsername,
                            Password = IOSNativeSettings.CustomPassword,
                            AutoLogin = IOSNativeSettings.CustomAutoLogin,
                        });
                    }
                    else
                    {
                        MajDebug.LogWarning($"[iOSNativeSettings]Invalid uri: {IOSNativeSettings.MajnetApi}, ignored.");
                    }
                }
#endif
                for (var i = 0; i < apiEndpoints.Count; i++)
                {
                    var apiEndpoint = apiEndpoints[i];
                    var uri = apiEndpoint.Url;
                    if (uri is null || string.IsNullOrEmpty(apiEndpoint.Name))
                    {
                        continue;
                    }

                    if (uri.OriginalString.LastOrDefault() != '/')
                    {
                        apiEndpoint = new()
                        {
                            Name = apiEndpoint.Name,
                            Url = new Uri(uri.OriginalString + "/"),
                            Username = apiEndpoint.Username,
                            Password = apiEndpoint.Password,
                        };
                    }

                    apiEndpoint.RuntimeConfig.AuthUsername = apiEndpoint.Username;
                    apiEndpoint.RuntimeConfig.AuthPassword = apiEndpoint.Password;
                    buffer.Add(apiEndpoint);
                }

                ApiEndpoints = buffer.GroupBy(x => x.Url)
                                     .Select(x => x.FirstOrDefault())
                                     .Where(x => x is not null)
                                     .ToArray();
            }
        }
        static void InitOnlineMachineInfo()
        {
            var machineDescriptionPath = Path.Combine(RootPath, "machine_description.json");

            if (File.Exists(machineDescriptionPath))
            {
                var js = File.ReadAllText(machineDescriptionPath);
                MachineInfo? machineInfo;

                if (!Serializer.Json.TryDeserialize(js, out machineInfo, out var e, UserJsonReaderOption) || machineInfo is null)
                {
                    MajDebug.LogError($"Failed to read machine description from file\nException: {e}");
                }
                else
                {
                    MachineInfo = machineInfo;
                }
            }
            else
            {
                var json = Serializer.Json.Serialize(MachineInfo, UserJsonReaderOption);
                File.WriteAllText(machineDescriptionPath, json);
            }
        }
        static void InitRuntimeConfig()
        {
            if (File.Exists(_runtimeConfigPath))
            {
                var js = File.ReadAllText(_runtimeConfigPath);
                RuntimeConfig? setting;

                if (!Serializer.Json.TryDeserialize(js, out setting, out var e, UserJsonReaderOption) || setting is null)
                {
                    RuntimeConfig = new();
                    MajDebug.LogError($"Failed to read runtime config from file\nException: {e}");
                }
                else
                {
                    RuntimeConfig = setting;
                }
            }
            else
            {
                RuntimeConfig = new();

                var json = Serializer.Json.Serialize(RuntimeConfig, UserJsonReaderOption);
                File.WriteAllText(_runtimeConfigPath, json);
            }
        }
        static void InitOthers()
        {            

        }
#endregion

        static void TryDeleteDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            try
            {
                Directory.Delete(path, true);
            }
            catch (Exception e)
            {
                MajDebug.LogWarning($"Failed to clear cache folder: {path}");
                MajDebug.LogException(e);
            }
        }

        static void OnApplicationQuit(object? sender, EventArgs? e)
        {
            GameManager.OnAppQuit -= OnApplicationQuit;
            GameManager.OnSave -= OnSave;
            SharedHttpClient.CancelPendingRequests();
            SharedHttpClient.Dispose();

            _globalCTS.Cancel();
#if UNITY_STANDALONE_WIN
            WinHidManager.QuitThisBs();
#elif UNITY_STANDALONE_OSX
            MacHidManager.QuitThisBs();
#endif
        }
        static void OnSave(object? sender, EventArgs? e)
        {
            //var listConfig = RuntimeConfig.List;
            //listConfig.SelectedSongIndex = SongStorage.WorkingCollection.Index;
            //listConfig.SelectedDir = SongStorage.CollectionIndex;

            var json = Serializer.Json.Serialize(Settings, UserJsonReaderOption);
            var json2 = Serializer.Json.Serialize(RuntimeConfig, UserJsonReaderOption);

            File.WriteAllText(SettingsPath, json);
            File.WriteAllText(_runtimeConfigPath, json2);

            MajDebug.FlushLog();
        }


        static void CreateDirectoryIfNotExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }
}