using System;
using System.Linq;
using System.Net;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;
using MajdataPlay.Net;
using MajdataPlay.Net.Curl;
using MajdataPlay.Settings;
using MajdataPlay.Settings.Runtime;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Misc.Support;
using MajdataPlay.Utils;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Base
{
    /// <summary>
    /// Covers the startup steps of <see cref="MajEnv"/> against temporary storage. The steps are invoked one by one
    /// because the full initialization runs only when the player starts.
    /// </summary>
    public class MajEnvTests
    {
        /// <summary>The temporary storage.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>The scope restoring the static environment.</summary>
        private MajEnvScope _environment = null!;

        /// <summary>The application root inside the temporary storage.</summary>
        private StorageDirectory _root = null!;

        /// <summary>The proxy configured on the shared HTTP handler before the test.</summary>
        private IWebProxy? _savedProxy;

        /// <summary>Whether the shared HTTP handler used a proxy before the test.</summary>
        private bool _savedUseProxy;

        /// <summary>Gets the shared HTTP handler.</summary>
        private static CurlHttpMessageHandler Handler => PrivateMember.GetStaticField<CurlHttpMessageHandler>(typeof(MajEnv), "s_httpClientHandler");

        /// <summary>Points the environment at a temporary layout.</summary>
        [SetUp]
        public void SetUp()
        {
            _temporary = new TemporaryDirectory();
            _environment = new MajEnvScope();
            _root = _environment.UseTemporaryLayout(_temporary);
            _savedProxy = Handler.Proxy;
            _savedUseProxy = Handler.UseProxy;
        }

        /// <summary>Restores the environment.</summary>
        [TearDown]
        public void TearDown()
        {
            Handler.Proxy = _savedProxy;
            Handler.UseProxy = _savedUseProxy;
            _environment.Dispose();
            _temporary.Dispose();
        }

        /// <summary>Invokes a private startup step.</summary>
        /// <param name="name">The step name.</param>
        /// <param name="arguments">The step arguments.</param>
        private static void Step(string name, params object?[] arguments)
        {
            PrivateMember.InvokeStatic(typeof(MajEnv), name, arguments);
        }

        /// <summary>Checks the constants and static services of the environment.</summary>
        [Test]
        public void Constants_DescribeRuntime()
        {
            Assert.That(MajEnv.IsEditor, Is.True);
            Assert.That(MajEnv.FRAME_LENGTH_SEC, Is.EqualTo(1f / 60));
            Assert.That(MajEnv.FRAME_LENGTH_MSEC, Is.EqualTo(1000f / 60).Within(1e-4f));
            Assert.That(MajEnv.SupportedAudioFormats, Does.Contain(".mp3"));
            Assert.That(MajEnv.SupportedVideoFormats, Does.Contain(".mp4"));
#if UNITY_ANDROID
            Assert.That(MajEnv.HTTP_USER_AGENT, Does.StartWith("MajdataPlay Android/"));
#elif UNITY_IOS
            Assert.That(MajEnv.HTTP_USER_AGENT, Does.StartWith("MajdataPlay iOS/"));
#else
            Assert.That(MajEnv.HTTP_USER_AGENT, Does.StartWith("MajdataPlay/"));
#endif
            Assert.That(MajEnv.Randomizer, Is.SameAs(MajEnv.Randomizer));
            Assert.That(MajEnv.GameProcess.Id, Is.GreaterThan(0));
            Assert.That(MajEnv.GlobalCT.IsCancellationRequested, Is.False);
            Assert.That(MajEnv.SharedHttpClient, Is.Not.SameAs(MajEnv.BackgroundHttpClient));
            Assert.That(MajEnv.SharedHttpClient.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(MajEnv.HTTP_TIMEOUT_MS)));
            Assert.That(MajEnv.UserJsonReaderOption.Converters, Is.Not.Empty);
            Assert.That(MajInstances.GameVersion.ToString(), Is.EqualTo("0.1.51"));
        }

        /// <summary>Checks that the writable application directories are created below the root and cache.</summary>
        [Test]
        public void InitDirectories_CreatesLayout()
        {
            var persistent = _temporary.Directory.CreateDirectory("Persistent");
            _environment.Set(nameof(MajEnv.PersistentDirectory), persistent);

            Step("InitDirectories");

            Assert.That(MajEnv.RuntimeDirectory.Location, Is.EqualTo(persistent.FindDirectory("Runtime")!.Location));
            Assert.That(MajEnv.CacheDirectory.FindDirectory("Runtime"), Is.Null);
            Assert.That(MajEnv.CacheDirectory.FindDirectory("Net"), Is.Not.Null);
            Assert.That(MajEnv.TempDirectory.Exists, Is.True);
            Assert.That(_root.FindDirectory("MaiCharts"), Is.Not.Null);
            Assert.That(_root.FindDirectory("Skins"), Is.Not.Null);
            Assert.That(_root.FindDirectory("Logs"), Is.Not.Null);
            Assert.That(_root.FindDirectory("RecordOutputs"), Is.Not.Null);
        }

        /// <summary>Checks that the legacy cache runtime directory is merged into the runtime directory and then deleted.</summary>
        [Test]
        public void MigrateLegacyRuntimeDirectory_MovesEntriesAndDeletesLegacy()
        {
            var legacy = MajEnv.CacheDirectory.CreateDirectory("Runtime");
            legacy.CreateFile("config.json").WriteAllText("legacy config");
            legacy.CreateFile("MajFavorites.db").WriteAllText("legacy favorites");
            legacy.CreateDirectory("Nested").CreateFile("data.bin").WriteAllText("nested");
            MajEnv.RuntimeDirectory.CreateFile("config.json").WriteAllText("current config");

            Step("MigrateLegacyRuntimeDirectory");

            var runtime = MajEnv.RuntimeDirectory;
            Assert.That(MajEnv.CacheDirectory.FindDirectory("Runtime"), Is.Null);
            Assert.That(runtime.FindFile("config.json")!.ReadAllText(), Is.EqualTo("current config"));
            Assert.That(runtime.FindFile("MajFavorites.db")!.ReadAllText(), Is.EqualTo("legacy favorites"));
            Assert.That(runtime.FindFileAt("Nested/data.bin")!.ReadAllText(), Is.EqualTo("nested"));
        }

        /// <summary>Checks that the migration leaves the runtime directory untouched when no legacy directory exists.</summary>
        [Test]
        public void MigrateLegacyRuntimeDirectory_WithoutLegacy_DoesNothing()
        {
            MajEnv.RuntimeDirectory.CreateFile("config.json").WriteAllText("current config");

            Step("MigrateLegacyRuntimeDirectory");

            Assert.That(MajEnv.RuntimeDirectory.FindFile("config.json")!.ReadAllText(), Is.EqualTo("current config"));
            Assert.That(MajEnv.CacheDirectory.FindDirectory("Runtime"), Is.Null);
        }

        /// <summary>Checks that the language directory is only reported while the streaming assets contain it.</summary>
        [Test]
        public void LangDirectory_RequiresStreamingAssets()
        {
            Assert.That(MajEnv.LangDirectory, Is.Null);

            _root.CreateDirectory("StreamingAssets");
            Assert.That(MajEnv.LangDirectory, Is.Null);

            _root.FindDirectory("StreamingAssets")!.CreateDirectory("Langs");
            Assert.That(MajEnv.LangDirectory, Is.Not.Null);
        }

        /// <summary>Checks that the first startup writes default settings and clamps judge distances.</summary>
        [Test]
        public void InitUserSettings_CreatesDefaults()
        {
            Step("InitUserSettings");

            var file = _root.FindFile("settings.json");
            Assert.That(file, Is.Not.Null);
            Assert.That(MajEnv.SettingsPath.Location, Is.EqualTo(file!.Location));
            var json = JObject.Parse(file.ReadAllText());
            Assert.That(json["Game"], Is.Not.Null);
            Assert.That(MajEnv.Settings.Display.InnerJudgeDistance, Is.EqualTo(1f));
            Assert.That(MajDebug.MinLogLevel, Is.EqualTo(MajEnv.Settings.Debug.DebugLevel));
        }

        /// <summary>Checks that stored settings are loaded and sanitized.</summary>
        [Test]
        public void InitUserSettings_LoadsAndSanitizes()
        {
            _root.CreateFile("settings.json").WriteAllText(
                "{\"Game\":{\"TapSpeed\":8.5},\"Display\":{\"InnerJudgeDistance\":4,\"OuterJudgeDistance\":-1}," +
                "\"Debug\":{\"DebugLevel\":\"Warning\"}," +
                "\"IO\":{\"InputDevice\":{\"ButtonRing\":{\"PollingRateMs\":-5,\"DebounceThresholdMs\":-1},\"TouchPanel\":{\"PollingRateMs\":-2,\"DebounceThresholdMs\":-3}}}}");

            Step("InitUserSettings");

            var settings = MajEnv.Settings;
            Assert.That(settings.Game.TapSpeed, Is.EqualTo(8.5f));
            Assert.That(settings.Display.InnerJudgeDistance, Is.EqualTo(1f));
            Assert.That(settings.Display.OuterJudgeDistance, Is.EqualTo(0f));
#if UNITY_STANDALONE
            Assert.That(settings.IO.InputDevice.ButtonRing.PollingRateMs, Is.EqualTo(0));
            Assert.That(settings.IO.InputDevice.ButtonRing.DebounceThresholdMs, Is.EqualTo(0));
            Assert.That(settings.IO.InputDevice.TouchPanel.PollingRateMs, Is.EqualTo(0));
            Assert.That(settings.IO.InputDevice.TouchPanel.DebounceThresholdMs, Is.EqualTo(0));
#endif
#if UNITY_IOS
            Assert.That(MajDebug.MinLogLevel, Is.EqualTo(LogLevel.Info), "iOS does not read DebugLevel from settings.json");
#else
            Assert.That(MajDebug.MinLogLevel, Is.EqualTo(LogLevel.Warning));
#endif
        }

        /// <summary>Checks that legacy rendering keys are migrated when settings.json is read and that the next save writes the current layout.</summary>
        [Test]
        public void InitUserSettings_MigratesLegacyRenderingAndSavesCurrentLayout()
        {
            _environment.Set(nameof(MajEnv.RuntimeConfig), new RuntimeConfig());
            _root.CreateFile("settings.json").WriteAllText(
                "{\"Display\":{\"Skin\":\"custom\",\"RenderQuality\":\"High\",\"RenderScale\":100,\"FPSLimit\":60,\"VSync\":false}}");

            Step("InitUserSettings");
            Step("OnSave", null, null);

            var settings = MajEnv.Settings;
            Assert.That(settings.Display.Skin, Is.EqualTo("custom"));
            Assert.That(settings.Rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Beautiful));
            Assert.That(settings.Rendering.FPSLimit, Is.EqualTo(60));
            Assert.That(settings.Rendering.VSync, Is.False);
            Assert.That(_root.FindFile("settings.json.bak"), Is.Null);
            var saved = JObject.Parse(_root.FindFile("settings.json")!.ReadAllText());
            var display = (JObject)saved["Display"]!;
            Assert.That(display.ContainsKey("RenderQuality") || display.ContainsKey("RenderScale") ||
                        display.ContainsKey("FPSLimit") || display.ContainsKey("VSync"), Is.False);
            Assert.That(saved["Rendering"]!["RenderQuality"]!.Value<string>(), Is.EqualTo("Beautiful"));
            Assert.That(saved["Rendering"]!["FPSLimit"]!.Value<int>(), Is.EqualTo(60));
        }

        /// <summary>Checks that unreadable settings are replaced by defaults and backed up without overwriting older backups.</summary>
        [Test]
        public void InitUserSettings_BacksUpCorruptFile()
        {
            _root.CreateFile("settings.json").WriteAllText("{ broken");
            _root.CreateFile("settings.json.bak").WriteAllText("older");

            Step("InitUserSettings");

            Assert.That(MajEnv.Settings.Game.TapSpeed, Is.EqualTo(7.5f));
            Assert.That(_root.FindFile("settings.json.bak")!.ReadAllText(), Is.EqualTo("older"));
            Assert.That(_root.FindFile("settings.json.bak.bak")!.ReadAllText(), Is.EqualTo("{ broken"));
        }

        /// <summary>Checks that the runtime configuration is created, loaded and replaced when unreadable.</summary>
        [Test]
        public void InitRuntimeConfig_CreatesLoadsAndRecovers()
        {
            Step("InitRuntimeConfig");
            var file = MajEnv.RuntimeDirectory.FindFile("config.json");
            Assert.That(file, Is.Not.Null);
            Assert.That(MajEnv.RuntimeConfig, Is.Not.Null);

            var selectedId = System.Guid.Parse("4d2783ac-a6ef-40f4-b648-b043b4c58608");
            file!.WriteAllText("{\"List\":{\"SelectedDirGuid\":\"4d2783ac-a6ef-40f4-b648-b043b4c58608\",\"SelectedSongHash\":\"h\"}}");
            Step("InitRuntimeConfig");
            Assert.That(MajEnv.RuntimeConfig!.List.SelectedDirGuid, Is.EqualTo(selectedId));
            Assert.That(MajEnv.RuntimeConfig.List.SelectedSongHash, Is.EqualTo("h"));

            file.WriteAllText("not json");
            Step("InitRuntimeConfig");
            Assert.That(MajEnv.RuntimeConfig!.List.SelectedDirGuid, Is.EqualTo(System.Guid.Empty));
            Assert.That(MajEnv.RuntimeConfig.List.SelectedSongHash, Is.Empty);
        }

        /// <summary>Checks that the machine description is created with defaults, loaded, and kept when unreadable.</summary>
        [Test]
        public void InitOnlineMachineInfo_CreatesAndLoads()
        {
            Step("InitOnlineMachineInfo");
            var file = _root.FindFile("machine_description.json");
            Assert.That(file, Is.Not.Null);
            Assert.That(JObject.Parse(file!.ReadAllText())["Name"]!.Value<string>(), Is.EqualTo(MajEnv.MachineInfo.Name));

            file.WriteAllText("{\"Name\":\"Cabinet\",\"Description\":\"Corner\"}");
            Step("InitOnlineMachineInfo");
            Assert.That(MajEnv.MachineInfo.Name, Is.EqualTo("Cabinet"));
            Assert.That(MajEnv.MachineInfo.Description, Is.EqualTo("Corner"));

            file.WriteAllText("[broken");
            Step("InitOnlineMachineInfo");
            Assert.That(MajEnv.MachineInfo.Name, Is.EqualTo("Cabinet"));
        }

        /// <summary>Checks that endpoints get a trailing slash, keep their credentials and are deduplicated by URL.</summary>
        [Test]
        public void InitOnlineEndpoints_NormalizesEndpoints()
        {
            MajEnv.Settings.Online.ApiEndpoints = new[]
            {
                new ApiEndpoint { Name = "Main", Url = new Uri("https://example.invalid/api"), Username = "u", Password = "p" },
                new ApiEndpoint { Name = "Duplicate", Url = new Uri("https://example.invalid/api/") },
                new ApiEndpoint { Name = string.Empty, Url = new Uri("https://unnamed.invalid/") },
                new ApiEndpoint { Name = "NoUrl", Url = null! },
                new ApiEndpoint { Name = "Other", Url = new Uri("https://other.invalid/v1/"), Username = "x", Password = "y" },
            };

            Step("InitOnlineEndpoints");

            var endpoints = MajEnv.ApiEndpoints!;
            Assert.That(endpoints.Select(endpoint => endpoint.Name), Is.EqualTo(new[] { "Main", "Other" }));
            Assert.That(endpoints[0].Url.OriginalString, Is.EqualTo("https://example.invalid/api/"));
            Assert.That(endpoints[0].RuntimeConfig.AuthUsername, Is.EqualTo("u"));
            Assert.That(endpoints[0].RuntimeConfig.AuthPassword, Is.EqualTo("p"));
            Assert.That(endpoints[1].RuntimeConfig.AuthUsername, Is.EqualTo("x"));
        }

#if UNITY_STANDALONE
        /// <summary>Checks the proxy configuration for disabled, invalid, unsupported and credentialed proxies.</summary>
        [Test]
        public void InitNetwork_ConfiguresProxy()
        {
            ConfigureProxy(false, string.Empty);
            Step("InitNetwork");
            Assert.That(Handler.UseProxy, Is.False);

            Handler.UseProxy = true;
            var before = new WebProxy("http://before.invalid:1");
            Handler.Proxy = before;
            ConfigureProxy(true, "not a uri");
            Step("InitNetwork");
            Assert.That(Handler.Proxy, Is.SameAs(before));

            ConfigureProxy(true, "socks5://proxy.invalid:1080");
            Step("InitNetwork");
            Assert.That(Handler.Proxy, Is.SameAs(before));

            ConfigureProxy(true, string.Empty);
            Step("InitNetwork");
            Assert.That(Handler.Proxy, Is.SameAs(before));

            ConfigureProxy(true, "http://user:secret@proxy.invalid:8080");
            Step("InitNetwork");
            var credentialed = (WebProxy)Handler.Proxy!;
            var credential = credentialed.Credentials!.GetCredential(new Uri("http://proxy.invalid:8080"), "Basic");
            Assert.That(credentialed.Address!.Host, Is.EqualTo("proxy.invalid"));
            Assert.That(credential.UserName, Is.EqualTo("user"));
            Assert.That(credential.Password, Is.EqualTo("secret"));

            ConfigureProxy(true, "https://onlyuser@proxy.invalid:8443");
            Step("InitNetwork");
            Assert.That(((WebProxy)Handler.Proxy!).Credentials, Is.Null);
        }

        /// <summary>Replaces the proxy settings, which are init-only in the settings model.</summary>
        /// <param name="useProxy">Whether a proxy is used.</param>
        /// <param name="proxy">The proxy URL.</param>
        private static void ConfigureProxy(bool useProxy, string proxy)
        {
            var online = MajEnv.Settings.Online;
            PrivateMember.SetProperty(online, nameof(OnlineOptions.UseProxy), useProxy);
            PrivateMember.SetProperty(online, nameof(OnlineOptions.Proxy), proxy);
        }
#endif

        /// <summary>Checks that saving writes the settings and runtime configuration, recreating deleted files.</summary>
        [Test]
        public void OnSave_WritesConfiguration()
        {
            _environment.Set(nameof(MajEnv.RuntimeConfig), new RuntimeConfig());
            MajEnv.Settings.Game.TapSpeed = 6.25f;
            MajEnv.RuntimeConfig!.List.SelectedSongHash = "saved";

            Step("OnSave", null, null);

            var settings = Serializer.Json.Deserialize<GameSetting>(_root.FindFile("settings.json")!.ReadAllText(), MajEnv.UserJsonReaderOption)!;
            var runtime = Serializer.Json.Deserialize<RuntimeConfig>(MajEnv.RuntimeDirectory.FindFile("config.json")!.ReadAllText())!;
            Assert.That(settings.Game.TapSpeed, Is.EqualTo(6.25f));
            Assert.That(runtime.List.SelectedSongHash, Is.EqualTo("saved"));
            Assert.That(MajEnv.SettingsPath.Location, Is.EqualTo(_root.FindFile("settings.json")!.Location));

            MajEnv.Settings.Game.TapSpeed = 5f;
            Step("OnSave", null, null);
            var shorter = _root.FindFile("settings.json")!.ReadAllText();
            Assert.That(Serializer.Json.Deserialize<GameSetting>(shorter, MajEnv.UserJsonReaderOption)!.Game.TapSpeed, Is.EqualTo(5f));
        }

        /// <summary>Checks that the cache cleanup helper deletes existing directories and ignores missing ones.</summary>
        [Test]
        public void TryDeleteDirectory_DeletesExistingDirectory()
        {
            var directory = _root.CreateDirectory("ToDelete");
            directory.CreateFile("file.bin");
            var missing = new StorageDirectory(_root.FileSystem, System.IO.Path.Combine(_root.Location, "Missing"));

            Step("TryDeleteDirectory", directory);
            Step("TryDeleteDirectory", missing);
            Step("TryDeleteDirectory", new object?[] { null });

            Assert.That(directory.Exists, Is.False);
        }

        /// <summary>Checks that the file helper finds an existing file or creates a missing one.</summary>
        [Test]
        public void FindOrCreateFile_ReusesExistingFile()
        {
            var created = PrivateMember.InvokeStatic<StorageFile>(typeof(MajEnv), "FindOrCreateFile", _root, "new.json");
            created.WriteAllText("content");
            var found = PrivateMember.InvokeStatic<StorageFile>(typeof(MajEnv), "FindOrCreateFile", _root, "new.json");

            Assert.That(found.Location, Is.EqualTo(created.Location));
            Assert.That(found.ReadAllText(), Is.EqualTo("content"));
        }
    }
}
