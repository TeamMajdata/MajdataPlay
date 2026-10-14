using Cysharp.Threading.Tasks;
#if UNITY_STANDALONE
using HidSharp.Platform.Windows;
using HidSharp.Platform.MacOS;
#endif
using MajdataPlay.Buffers;
using MajdataPlay.IO.Storage;
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

        public static readonly string[] SupportedVideoFormats = new string[]
        {
            ".webm",
            ".mkv",
            ".mp4",
            ".mov"
        };
        public static readonly string[] SupportedAudioFormats = new string[]
        {
            ".mp3",
            ".opus",
            ".ogg",
            ".aac",
            ".wav"
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

        /// <summary>Gets the local application data root, initialized before game services start.</summary>
        public static StorageDirectory RootPath { get; private set; } = null!;
        /// <summary>Gets the packaged or extracted streaming assets directory without creating it.</summary>
        public static StorageDirectory AssetsPath { get; private set; } = null!;
        /// <summary>Gets the local cache directory.</summary>
        public static StorageDirectory CachePath { get; private set; } = null!;
        /// <summary>Gets the temporary extraction directory under the cache root.</summary>
        public static StorageDirectory TempPath { get; private set; } = null!;
        /// <summary>Gets the chart collection root.</summary>
        public static StorageDirectory ChartPath { get; private set; } = null!;
        /// <summary>Gets the user settings file, which may not exist on first startup.</summary>
        public static StorageFile SettingsPath { get; private set; } = null!;
        /// <summary>Gets the custom skin directory.</summary>
        public static StorageDirectory SkinPath { get; private set; } = null!;
        /// <summary>Gets the runtime log directory.</summary>
        public static StorageDirectory LogsPath { get; private set; } = null!;
        /// <summary>Gets the language resources directory without creating it.</summary>
        public static StorageDirectory LangPath { get; private set; } = null!;
        /// <summary>Gets the local SQLite score database file.</summary>
        public static StorageFile ScoreDBPath { get; private set; } = null!;
        /// <summary>Gets the legacy JSON score file consumed by score migration.</summary>
        public static StorageFile LegacyScoreDBPath { get; private set; } = null!;
        /// <summary>Gets the local SQLite favorites database file.</summary>
        public static StorageFile FavoriteDBPath { get; private set; } = null!;
        /// <summary>Gets the current runtime log file.</summary>
        public static StorageFile LogPath { get; private set; } = null!;
        /// <summary>Gets the directory receiving recorded audio and video files.</summary>
        public static StorageDirectory RecordOutputsPath { get; private set; } = null!;
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
        /// <summary>Stores the runtime configuration file under the local cache directory.</summary>
        static StorageFile _runtimeConfigPath = null!;
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

        /// <summary>Resolves the platform's local application paths into typed storage handles without creating resources.</summary>
        /// <exception cref="NotImplementedException">The current platform has no application path layout.</exception>
        /// <exception cref="UnauthorizedAccessException">Accessing the legacy Android storage root was denied.</exception>
        /// <exception cref="IOException">Resolving or creating the legacy Android storage root failed.</exception>
        static void InitPath()
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            RootPath = FileSystem.Directory.Open(Path.Combine(Application.dataPath, "../"));
            AssetsPath = FileSystem.Directory.Open(Application.streamingAssetsPath);
            CachePath = FileSystem.Directory.Open(Path.Combine(RootPath.Location, "Cache"));
#elif UNITY_ANDROID
            var versionClass = AndroidJNI.FindClass("android/os/Build$VERSION");
            var fieldID = AndroidJNI.GetStaticFieldID(versionClass, "SDK_INT", "I");
            AndroidSdkVersion = AndroidJNI.GetStaticIntField(versionClass, fieldID);
            using var activity = AndroidRuntime.CurrentActivity;
            using var appInfo = activity.GetApplicationInfo();
            TargetSdkVersion = appInfo!.TargetSdkVersion;

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
                    RootPath = FileSystem.Directory.Open("/sdcard/Documents/MajdataPlay");
                    if (!RootPath.Exists)
                    {
                        FileSystem.Directory.Create(RootPath.Location);
                    }
                    var noMediaFlag = FileSystem.File.Open(Path.Combine(RootPath.Location, ".nomedia"));
                    if (!noMediaFlag.Exists)
                    {
                        try
                        {
                            FileSystem.File.Create(noMediaFlag.Location);
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
                    AssetsPath = FileSystem.Directory.Open(Path.Combine(RootPath.Location, "ExtStreamingAssets/"));
                }
                else
                {
                    RootPath = FileSystem.Directory.Open(Application.persistentDataPath);
                    AssetsPath = FileSystem.Directory.Open(Path.Combine(Application.persistentDataPath, "ExtStreamingAssets/"));
                }
            }
            else // >= Android 11
            {
                RootPath = FileSystem.Directory.Open(Application.persistentDataPath);
                AssetsPath = FileSystem.Directory.Open(Path.Combine(Application.persistentDataPath, "ExtStreamingAssets/"));
            }
                
            CachePath = FileSystem.Directory.Open(Application.temporaryCachePath);
#elif UNITY_IOS
            RootPath = FileSystem.Directory.Open(Application.persistentDataPath);
            AssetsPath = FileSystem.Directory.Open(Path.Combine(Application.persistentDataPath, "ExtStreamingAssets/"));
            CachePath = FileSystem.Directory.Open(Application.temporaryCachePath);
#else
            throw new NotImplementedException();
#endif
            _runtimeConfigPath = FileSystem.File.Open(Path.Combine(CachePath.Location, "Runtime", "config.json"));
            ChartPath = FileSystem.Directory.Open(Path.Combine(RootPath.Location, "MaiCharts"));
            SettingsPath = FileSystem.File.Open(Path.Combine(RootPath.Location, "settings.json"));
            SkinPath = FileSystem.Directory.Open(Path.Combine(RootPath.Location, "Skins"));
            LogsPath = FileSystem.Directory.Open(Path.Combine(RootPath.Location, "Logs"));
            LangPath = FileSystem.Directory.Open(Path.Combine(AssetsPath.Location, "Langs"));
            LegacyScoreDBPath = FileSystem.File.Open(Path.Combine(RootPath.Location,
                "MajDatabase.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db"));
            ScoreDBPath = FileSystem.File.Open(Path.Combine(RootPath.Location, "MajScores.db"));
            FavoriteDBPath = FileSystem.File.Open(Path.Combine(CachePath.Location, "Runtime", "MajFavorites.db"));
            LogPath = FileSystem.File.Open(Path.Combine(LogsPath.Location, "MajPlayRuntime.log"));
            RecordOutputsPath = FileSystem.Directory.Open(Path.Combine(RootPath.Location, "RecordOutputs"));
            TempPath = FileSystem.Directory.Open(Path.Combine(CachePath.Location, "Temp"));
        }
        /// <summary>Creates writable local directories while leaving missing streaming assets untouched.</summary>
        /// <exception cref="UnauthorizedAccessException">Creating an application directory was denied.</exception>
        /// <exception cref="IOException">An application directory could not be created.</exception>
        static void CheckDirectories()
        {
            CreateDirectoryIfNotExists(SkinPath);
            CreateDirectoryIfNotExists(CachePath);
            CachePath.CreateDirectory("Runtime");
            CachePath.CreateDirectory("Net");
            CreateDirectoryIfNotExists(TempPath);
            CreateDirectoryIfNotExists(ChartPath);
            CreateDirectoryIfNotExists(RecordOutputsPath);
            CreateDirectoryIfNotExists(LogsPath);
            RootPath.CreateDirectory("Runtime");
        }
        static void InitLogger()
        {
            var oldLogPath = LogPath.Location + ".old";

            if (FileSystem.File.Open(oldLogPath).Exists)
            {
                FileSystem.File.Open(oldLogPath).Delete();
            }
            if (LogPath.Exists)
            {
                FileSystem.File.Move(LogPath.Location, oldLogPath);
            }
            var fileWriter = new StreamWriter(FileSystem.File.OpenWrite(LogPath.Location, append: true), Encoding.UTF8);
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
            if (SettingsPath.Exists)
            {
                var js = SettingsPath.ReadAllText();
                GameSetting? setting;


                if (!Serializer.Json.TryDeserialize(js, out setting, out var e, UserJsonReaderOption) || setting is null)
                {
                    Settings = new();
                    MajDebug.LogError($"Failed to read setting from file\nException: {e}");
                    var bakFileName = $"{SettingsPath.Location}.bak";
                    while (FileSystem.File.Open(bakFileName).Exists)
                    {
                        bakFileName = $"{bakFileName}.bak";
                    }

                    try
                    {
                        FileSystem.File.Copy(SettingsPath.Location, bakFileName, overwrite: true);
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
                WriteLocalText(SettingsPath, json);
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
                TryDeleteDirectory(CachePath.FindDirectory("Net"));
                TryDeleteDirectory(CachePath.FindDirectory("View"));
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
            var machineDescriptionPath = FileSystem.File.Open(Path.Combine(RootPath.Location, "machine_description.json"));

            if (machineDescriptionPath.Exists)
            {
                var js = machineDescriptionPath.ReadAllText();
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
                WriteLocalText(machineDescriptionPath, json);
            }
        }
        static void InitRuntimeConfig()
        {
            if (_runtimeConfigPath.Exists)
            {
                var js = _runtimeConfigPath.ReadAllText();
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
                WriteLocalText(_runtimeConfigPath, json);
            }
        }
        static void InitOthers()
        {            

        }
#endregion

        /// <summary>Attempts to remove an optional cache directory and logs deletion failures.</summary>
        /// <param name="directory">The cache directory, or null when it is absent.</param>
        static void TryDeleteDirectory(StorageDirectory? directory)
        {
            if (directory is null || !directory.Exists)
            {
                return;
            }

            try
            {
                directory.Delete(recursive: true);
            }
            catch (Exception e)
            {
                MajDebug.LogWarning($"Failed to clear cache folder: {directory.Location}");
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

            WriteLocalText(SettingsPath, json);
            WriteLocalText(_runtimeConfigPath, json2);

            MajDebug.FlushLog();
        }


        /// <summary>Creates or replaces a local configuration file through one storage output stream.</summary>
        /// <param name="file">The local configuration file whose parent directory already exists.</param>
        /// <param name="text">The UTF-8 text to write without a byte order mark.</param>
        /// <exception cref="UnauthorizedAccessException">Writing the configuration was denied.</exception>
        /// <exception cref="IOException">The configuration could not be opened or written.</exception>
        private static void WriteLocalText(StorageFile file, string text)
        {
            using var stream = FileSystem.File.OpenWrite(file.Location, overwrite: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(text);
        }

        /// <summary>Creates a missing application directory at its known local location.</summary>
        /// <param name="directory">The directory handle initialized from a local application path.</param>
        /// <exception cref="UnauthorizedAccessException">Creating the directory was denied.</exception>
        /// <exception cref="IOException">The directory could not be created.</exception>
        static void CreateDirectoryIfNotExists(StorageDirectory directory)
        {
            if (!directory.Exists)
            {
                FileSystem.Directory.Create(directory.Location);
            }
        }
    }
}
