using Cysharp.Threading.Tasks;
#if UNITY_STANDALONE
using HidSharp.Platform.Windows;
using HidSharp.Platform.MacOS;
#endif
using MajdataPlay.Buffers;
using MajdataPlay.IO.Storage;
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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using UnityEngine;
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

        /// <summary>Gets the application data root, initialized before game services start.</summary>
        /// <remarks>
        /// Children are resolved through this handle, never by combining <see cref="StorageDirectory.Location"/> with
        /// <see cref="Path"/>, so the root may later point at an opaque provider location such as an Android SAF tree.
        /// </remarks>
        public static StorageDirectory RootDirectory { get; private set; } = null!;
        /// <summary>Gets the persistent application data directory, which is always a native path.</summary>
        /// <remarks>
        /// Data which native libraries open by filename, such as the SQLite score database, lives here. On Windows, Linux
        /// and macOS it is <see cref="RootDirectory"/>; on Android and iOS it is the application-private persistent data
        /// directory, which stays native even when <see cref="RootDirectory"/> points at an Android SAF tree.
        /// </remarks>
        public static StorageDirectory PersistentDirectory { get; private set; } = null!;
        /// <summary>Gets the packaged or extracted streaming assets directory without creating it.</summary>
        public static StorageDirectory AssetsDirectory { get; private set; } = null!;
        /// <summary>Gets the local cache directory.</summary>
        public static StorageDirectory CacheDirectory { get; private set; } = null!;
        /// <summary>Gets the runtime data directory under the cache root, which holds the runtime configuration and favorites database.</summary>
        public static StorageDirectory RuntimeCacheDirectory { get; private set; } = null!;
        /// <summary>Gets the temporary extraction directory under the cache root.</summary>
        public static StorageDirectory TempDirectory { get; private set; } = null!;
        /// <summary>Gets the chart collection root.</summary>
        public static StorageDirectory ChartDirectory { get; private set; } = null!;
        /// <summary>Gets the user settings file, which is created with default settings on first startup.</summary>
        public static StorageFile SettingsPath { get; private set; } = null!;
        /// <summary>Gets the custom skin directory.</summary>
        public static StorageDirectory SkinDirectory { get; private set; } = null!;
        /// <summary>Gets the runtime log directory.</summary>
        public static StorageDirectory LogsDirectory { get; private set; } = null!;
        /// <summary>Finds the language resources directory inside the streaming assets, or null while it is absent.</summary>
        /// <exception cref="UnauthorizedAccessException">Querying the streaming assets was denied.</exception>
        /// <exception cref="IOException">Querying the streaming assets failed.</exception>
        public static StorageDirectory? LangDirectory
        {
            get
            {
                return AssetsDirectory.Exists ? AssetsDirectory.FindDirectory("Langs") : null;
            }
        }
        /// <summary>Gets the current runtime log file, created when the logger starts.</summary>
        public static StorageFile LogPath { get; private set; } = null!;
        /// <summary>Gets the directory receiving recorded audio and video files.</summary>
        public static StorageDirectory RecordOutputsDirectory { get; private set; } = null!;
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
                if (s_randomizer is null)
                {
                    s_randomizer = new();
                }
                return s_randomizer;
            }
        }
        public static Thread MainThread { get; } = Thread.CurrentThread;
        public static Process GameProcess { get; } = Process.GetCurrentProcess();

        private static readonly CurlHttpMessageHandler s_httpClientHandler = new CurlHttpMessageHandler()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            Proxy = WebRequest.GetSystemWebProxy(),
            UseProxy = true,
            CookieContainer = new CookieContainer(),
            MaxConnectionsPerServer = 64,
        };
        public static HttpClient SharedHttpClient { get; } = new HttpClient(s_httpClientHandler)
        {
            Timeout = TimeSpan.FromMilliseconds(HTTP_TIMEOUT_MS),
            DefaultRequestHeaders =
            {
                UserAgent = { new ProductInfoHeaderValue("MajdataPlay", MajInstances.GameVersion.ToString()) },
            }
        };

        public static GameSetting Settings { get; private set; } = new();
        public static RuntimeConfig? RuntimeConfig { get; private set; }
        public static ApiEndpoint[]? ApiEndpoints { get; private set; }

        public static CancellationToken GlobalCT
        {
            get { return s_globalCTS.Token; }
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

        /// <summary>The user settings file name inside <see cref="RootDirectory"/>.</summary>
        private const string SettingsFileName = "settings.json";
        /// <summary>The runtime configuration file name inside <see cref="RuntimeCacheDirectory"/>.</summary>
        private const string RuntimeConfigFileName = "config.json";
        /// <summary>The machine description file name inside <see cref="RootDirectory"/>.</summary>
        private const string MachineDescriptionFileName = "machine_description.json";
        /// <summary>The current runtime log file name inside <see cref="LogsDirectory"/>.</summary>
        private const string LogFileName = "MajPlayRuntime.log";
        /// <summary>The rotated previous runtime log file name inside <see cref="LogsDirectory"/>.</summary>
        private const string OldLogFileName = "MajPlayRuntime.log.old";
        /// <summary>The content type requested for JSON files created on a provider.</summary>
        private const string JsonMimeType = "application/json";

        [ThreadStatic]
        private static Random? s_randomizer;
        private static readonly CancellationTokenSource s_globalCTS = new();


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ChangedSynchronizationContext()
        {
#if !UNITY_EDITOR
            SynchronizationContext.SetSynchronizationContext(new UniTaskSynchronizationContext());
#endif
        }
        #region Init core
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Init()
        {
            InitPath();
            InitDirectories();
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

        /// <summary>Resolves the platform's root, persistent, streaming assets, and cache directories, creating the writable roots.</summary>
        /// <remarks>
        /// Only Unity application paths are combined with <see cref="Path"/>; every child of a resolved handle is
        /// created or found through that handle so the layout survives a provider-backed root.
        /// </remarks>
        /// <exception cref="NotImplementedException">The current platform has no application path layout.</exception>
        /// <exception cref="UnauthorizedAccessException">Creating a writable root was denied.</exception>
        /// <exception cref="IOException">Resolving or creating a writable root failed.</exception>
        private static void InitPath()
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            RootDirectory = FileSystem.Directory.Open(Path.Combine(Application.dataPath, ".."));
            PersistentDirectory = RootDirectory;
            AssetsDirectory = FileSystem.Directory.Open(Application.streamingAssetsPath);
            CacheDirectory = RootDirectory.CreateDirectory("Cache");
#elif UNITY_ANDROID
            // The persistent directory stays native whatever the root is, so native libraries always have a filename to open.
            PersistentDirectory = FileSystem.Directory.Open(Application.persistentDataPath);
            if (AndroidRuntime.SdkVersion < 30 || AndroidRuntime.TargetSdkVersion < 30) // < Android 11
            {
                var androidStoragePermissions = new string[]
                {
                    UnityEngine.Android.Permission.ExternalStorageRead,
                    UnityEngine.Android.Permission.ExternalStorageWrite,
                };
                var isGranted = true;
                for (var i = 0; i < androidStoragePermissions.Length; i++)
                {
                    var flag = 0;
                    var permission = androidStoragePermissions[i];
                RECHECK_PERMISSION:
                    if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(permission))
                    {
                        switch (flag)
                        {
                            case 0:
                                UnityEngine.Android.Permission.RequestUserPermission(permission);
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
                    // The legacy shared root is a known native path, so its streaming assets path is computed from the same constant.
                    const string legacyRootPath = "/sdcard/Documents/MajdataPlay";
                    RootDirectory = FileSystem.Directory.Create(legacyRootPath);
                    if (RootDirectory.FindFile(".nomedia") is null)
                    {
                        try
                        {
                            RootDirectory.CreateFile(".nomedia");
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
                    AssetsDirectory = FileSystem.Directory.Open(Path.Combine(legacyRootPath, "ExtStreamingAssets"));
                }
                else
                {
                    RootDirectory = PersistentDirectory;
                    AssetsDirectory = FileSystem.Directory.Open(Path.Combine(Application.persistentDataPath, "ExtStreamingAssets"));
                }
            }
            else // >= Android 11
            {
                RootDirectory = PersistentDirectory;
                AssetsDirectory = FileSystem.Directory.Open(Path.Combine(Application.persistentDataPath, "ExtStreamingAssets"));
            }
            CacheDirectory = FileSystem.Directory.Create(Application.temporaryCachePath);
#elif UNITY_IOS
            PersistentDirectory = FileSystem.Directory.Open(Application.persistentDataPath);
            RootDirectory = PersistentDirectory;
            AssetsDirectory = FileSystem.Directory.Open(Path.Combine(Application.persistentDataPath, "ExtStreamingAssets"));
            CacheDirectory = FileSystem.Directory.Create(Application.temporaryCachePath);
#else
            throw new NotImplementedException();
#endif
        }
        /// <summary>Creates the writable application directories through their parent handles, leaving missing streaming assets untouched.</summary>
        /// <exception cref="UnauthorizedAccessException">Creating an application directory was denied.</exception>
        /// <exception cref="NotSupportedException">The root's backend cannot create directories.</exception>
        /// <exception cref="IOException">A file occupies an application directory name or creation failed.</exception>
        private static void InitDirectories()
        {
            RuntimeCacheDirectory = CacheDirectory.CreateDirectory("Runtime");
            CacheDirectory.CreateDirectory("Net");
            TempDirectory = CacheDirectory.CreateDirectory("Temp");
            ChartDirectory = RootDirectory.CreateDirectory("MaiCharts");
            SkinDirectory = RootDirectory.CreateDirectory("Skins");
            LogsDirectory = RootDirectory.CreateDirectory("Logs");
            RecordOutputsDirectory = RootDirectory.CreateDirectory("RecordOutputs");
            RootDirectory.CreateDirectory("Runtime");
        }
        /// <summary>Rotates the previous runtime log and directs log output to a new log file in <see cref="LogsDirectory"/>.</summary>
        /// <exception cref="UnauthorizedAccessException">Rotating or creating the log file was denied.</exception>
        /// <exception cref="NotSupportedException">The log directory's backend cannot rename or create files.</exception>
        /// <exception cref="IOException">The log file could not be rotated, created, or opened.</exception>
        private static void InitLogger()
        {
            LogsDirectory.FindFile(OldLogFileName)?.Delete();
            LogsDirectory.FindFile(LogFileName)?.Rename(OldLogFileName);
            LogPath = LogsDirectory.CreateFile(LogFileName, "text/plain");
            // The facade's append stream keeps the file readable by other processes while the game runs.
            var fileWriter = new StreamWriter(FileSystem.File.OpenWrite(LogPath.Location, append: true), Encoding.UTF8);
            fileWriter.AutoFlush = true;

            MajDebug.SetLogWriter(fileWriter);
        }
        private static void InitSystemSettings()
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
        /// <summary>Loads the user settings from <see cref="RootDirectory"/>, backing up an unreadable file or writing defaults on first startup.</summary>
        /// <exception cref="UnauthorizedAccessException">Reading or creating the settings file was denied.</exception>
        /// <exception cref="IOException">The settings file could not be read, created, or written.</exception>
        private static void InitUserSettings()
        {
            var settingsFile = RootDirectory.FindFile(SettingsFileName);
            if (settingsFile is not null)
            {
                var js = settingsFile.ReadAllText();
                GameSetting? setting;


                if (!Serializer.Json.TryDeserialize(js, out setting, out var e, UserJsonReaderOption) || setting is null)
                {
                    Settings = new();
                    MajDebug.LogError($"Failed to read setting from file\nException: {e}");
                    var bakFileName = $"{SettingsFileName}.bak";
                    while (RootDirectory.FindFile(bakFileName) is not null)
                    {
                        bakFileName = $"{bakFileName}.bak";
                    }

                    try
                    {
                        settingsFile.CopyTo(RootDirectory, bakFileName, overwrite: true);
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
                settingsFile = RootDirectory.CreateFile(SettingsFileName, JsonMimeType);
                WriteText(settingsFile, json);
            }
            SettingsPath = settingsFile;

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
        private static void ReadiOSNativeSettings()
        {
#if UNITY_IOS
            if (IOSNativeSettings.DebugNoCache)
            {
                TryDeleteDirectory(CacheDirectory.FindDirectory("Net"));
                TryDeleteDirectory(CacheDirectory.FindDirectory("View"));
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
        private static void InitNetwork()
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
                    s_httpClientHandler.Proxy = proxy;
                }
            }
            else
            {
                s_httpClientHandler.UseProxy = false;
                //_httpClientHandler.Proxy = null;
            }
#endif
        }
        private static void InitOnlineEndpoints()
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
        private static void InitOnlineMachineInfo()
        {
            var machineDescriptionFile = RootDirectory.FindFile(MachineDescriptionFileName);

            if (machineDescriptionFile is not null)
            {
                var js = machineDescriptionFile.ReadAllText();
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
                WriteText(RootDirectory.CreateFile(MachineDescriptionFileName, JsonMimeType), json);
            }
        }
        private static void InitRuntimeConfig()
        {
            var runtimeConfigFile = RuntimeCacheDirectory.FindFile(RuntimeConfigFileName);
            if (runtimeConfigFile is not null)
            {
                var js = runtimeConfigFile.ReadAllText();
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
                WriteText(RuntimeCacheDirectory.CreateFile(RuntimeConfigFileName, JsonMimeType), json);
            }
        }
        private static void InitOthers()
        {

        }
        #endregion

        /// <summary>Attempts to remove an optional cache directory and logs deletion failures.</summary>
        /// <param name="directory">The cache directory, or null when it is absent.</param>
        private static void TryDeleteDirectory(StorageDirectory? directory)
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

        private static void OnApplicationQuit(object? sender, EventArgs? e)
        {
            GameManager.OnAppQuit -= OnApplicationQuit;
            GameManager.OnSave -= OnSave;
            SharedHttpClient.CancelPendingRequests();
            SharedHttpClient.Dispose();

            s_globalCTS.Cancel();
#if UNITY_STANDALONE_WIN
            WinHidManager.QuitThisBs();
#elif UNITY_STANDALONE_OSX
            MacHidManager.QuitThisBs();
#endif
        }
        private static void OnSave(object? sender, EventArgs? e)
        {
            //var listConfig = RuntimeConfig.List;
            //listConfig.SelectedSongIndex = SongStorage.WorkingCollection.Index;
            //listConfig.SelectedDir = SongStorage.CollectionIndex;

            var json = Serializer.Json.Serialize(Settings, UserJsonReaderOption);
            var json2 = Serializer.Json.Serialize(RuntimeConfig, UserJsonReaderOption);

            // Re-resolve the files so a configuration deleted while running is recreated on every backend.
            SettingsPath = FindOrCreateFile(RootDirectory, SettingsFileName);
            WriteText(SettingsPath, json);
            WriteText(FindOrCreateFile(RuntimeCacheDirectory, RuntimeConfigFileName), json2);

            MajDebug.FlushLog();
        }


        /// <summary>Replaces an existing configuration file's content through one storage output stream.</summary>
        /// <param name="file">The existing configuration file on any backend.</param>
        /// <param name="text">The UTF-8 text to write without a byte order mark.</param>
        /// <exception cref="UnauthorizedAccessException">Writing the configuration was denied.</exception>
        /// <exception cref="FileNotFoundException">The provider configuration file no longer exists.</exception>
        /// <exception cref="IOException">The configuration could not be opened or written.</exception>
        private static void WriteText(StorageFile file, string text)
        {
            using var stream = FileSystem.File.OpenWrite(file.Location, overwrite: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(text);
        }

        /// <summary>Finds a JSON configuration file in a directory, or creates it empty so it can be written on any backend.</summary>
        /// <param name="directory">The existing directory owning the configuration file.</param>
        /// <param name="name">The configuration file name.</param>
        /// <returns>The existing or newly created file handle.</returns>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Querying or creating the file was denied.</exception>
        /// <exception cref="NotSupportedException">The directory's backend cannot create files.</exception>
        /// <exception cref="IOException">Querying or creating the file failed.</exception>
        private static StorageFile FindOrCreateFile(StorageDirectory directory, string name)
        {
            return directory.FindFile(name) ?? directory.CreateFile(name, JsonMimeType);
        }
    }
}
