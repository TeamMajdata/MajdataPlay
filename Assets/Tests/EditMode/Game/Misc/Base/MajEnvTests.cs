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
using SQLite;

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
            Assert.That(MajEnv.SharedHttpClient.Timeout, Is.EqualTo(MajEnv.HttpTimeout));
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

        /// <summary>Checks that the first startup writes default user and system settings and clamps judge distances.</summary>
        [Test]
        public void InitSettings_CreatesDefaults()
        {
            Step("InitSettings");

            var userFile = _root.FindFile("settings.json");
            var systemFile = _root.FindFile("system-settings.json");
            Assert.That(userFile, Is.Not.Null);
            Assert.That(systemFile, Is.Not.Null);
            Assert.That(MajEnv.UserSettingsPath.Location, Is.EqualTo(userFile!.Location));
            Assert.That(MajEnv.SystemSettingsPath.Location, Is.EqualTo(systemFile!.Location));
            var userJson = JObject.Parse(userFile.ReadAllText());
            var systemJson = JObject.Parse(systemFile.ReadAllText());
            Assert.That(userJson["Game"], Is.Not.Null);
            Assert.That(userJson["Rendering"], Is.Null);
            Assert.That(systemJson["Rendering"], Is.Not.Null);
            Assert.That(systemJson["Game"], Is.Null);
            Assert.That(systemJson["Network"]!["Online"]!["Machine"]!["Name"]!.Value<string>(), Is.EqualTo("MajdataPlay Client"));
            Assert.That(_root.FindFile("machine_description.json"), Is.Null);
            Assert.That(MajEnv.UserSettings.Judge.InnerJudgeDistance, Is.EqualTo(1f));
            Assert.That(MajDebug.MinLogLevel, Is.EqualTo(MajEnv.SystemSettings.Debug.DebugLevel));
        }

        /// <summary>Checks that stored settings are loaded from both files and sanitized.</summary>
        [Test]
        public void InitSettings_LoadsAndSanitizes()
        {
            _root.CreateFile("settings.json").WriteAllText(
                "{\"Game\":{\"TapSpeed\":8.5},\"Judge\":{\"InnerJudgeDistance\":4,\"OuterJudgeDistance\":-1}}");
            _root.CreateFile("system-settings.json").WriteAllText(
                "{\"Debug\":{\"DebugLevel\":\"Warning\"}," +
                "\"IO\":{\"InputDevice\":{\"ButtonRing\":{\"PollingRateMs\":-5,\"DebounceThresholdMs\":-1},\"TouchPanel\":{\"PollingRateMs\":-2,\"DebounceThresholdMs\":-3}}}}");

            Step("InitSettings");

            var user = MajEnv.UserSettings;
            var system = MajEnv.SystemSettings;
            Assert.That(user.Game.TapSpeed, Is.EqualTo(8.5f));
            Assert.That(user.Judge.InnerJudgeDistance, Is.EqualTo(1f));
            Assert.That(user.Judge.OuterJudgeDistance, Is.EqualTo(0f));
#if UNITY_STANDALONE
            Assert.That(system.IO.InputDevice.ButtonRing.PollingRateMs, Is.EqualTo(0));
            Assert.That(system.IO.InputDevice.ButtonRing.DebounceThresholdMs, Is.EqualTo(0));
            Assert.That(system.IO.InputDevice.TouchPanel.PollingRateMs, Is.EqualTo(0));
            Assert.That(system.IO.InputDevice.TouchPanel.DebounceThresholdMs, Is.EqualTo(0));
#endif
#if UNITY_IOS
            Assert.That(MajDebug.MinLogLevel, Is.EqualTo(LogLevel.Info), "iOS does not read DebugLevel from system-settings.json");
#else
            Assert.That(MajDebug.MinLogLevel, Is.EqualTo(LogLevel.Warning));
#endif
        }

        /// <summary>Checks that legacy rendering keys are migrated when settings.json is read and that the next save writes the current layouts.</summary>
        [Test]
        public void InitSettings_MigratesLegacyRenderingAndSavesCurrentLayout()
        {
            _environment.Set(nameof(MajEnv.RuntimeConfig), new RuntimeConfig());
            _root.CreateFile("settings.json").WriteAllText(
                "{\"Display\":{\"Skin\":\"custom\",\"RenderQuality\":\"High\",\"RenderScale\":100,\"FPSLimit\":60,\"VSync\":false}}");

            Step("InitSettings");
            Step("OnSave", null, null);

            Assert.That(MajEnv.UserSettings.Display.Skin, Is.EqualTo("custom"));
            var rendering = MajEnv.SystemSettings.Rendering;
            Assert.That(rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Beautiful));
            Assert.That(rendering.FPSLimit, Is.EqualTo(60));
            Assert.That(rendering.VSync, Is.False);
            Assert.That(_root.FindFile("settings.json.bak"), Is.Null);
            var savedUser = JObject.Parse(_root.FindFile("settings.json")!.ReadAllText());
            var savedSystem = JObject.Parse(_root.FindFile("system-settings.json")!.ReadAllText());
            var display = (JObject)savedUser["Display"]!;
            Assert.That(display.ContainsKey("RenderQuality") || display.ContainsKey("RenderScale") ||
                        display.ContainsKey("FPSLimit") || display.ContainsKey("VSync"), Is.False);
            Assert.That(savedUser.ContainsKey("Rendering"), Is.False);
            Assert.That(savedSystem["Rendering"]!["RenderQuality"]!.Value<string>(), Is.EqualTo("Beautiful"));
            Assert.That(savedSystem["Rendering"]!["FPSLimit"]!.Value<int>(), Is.EqualTo(60));
        }

        /// <summary>
        /// Checks that the system settings of a legacy settings.json are written to system-settings.json during startup,
        /// that settings.json keeps its legacy layout until the next save, and that the save leaves only user settings in it.
        /// </summary>
        [Test]
        public void InitSettings_SplitsLegacySettingsIntoSystemSettings()
        {
            _environment.Set(nameof(MajEnv.RuntimeConfig), new RuntimeConfig());
            const string legacy = "{\"Game\":{\"TapSpeed\":8.5,\"BGInfo\":\"None\"},\"Display\":{\"MainScreenScale\":0.8}," +
                                  "\"Audio\":{\"ForceMono\":true,\"Volume\":{\"Global\":0.9}},\"Debug\":{\"MaxQueuedFrames\":3,\"OffsetUnit\":\"Second\"}}";
            _root.CreateFile("settings.json").WriteAllText(legacy);

            Step("InitSettings");

            Assert.That(_root.FindFile("settings.json")!.ReadAllText(), Is.EqualTo(legacy));
            var system = JObject.Parse(_root.FindFile("system-settings.json")!.ReadAllText());
            Assert.That(system["Audio"]!["ForceMono"]!.Value<bool>(), Is.True);
            Assert.That(((JObject)system["Audio"]!).ContainsKey("Volume"), Is.False);
            Assert.That(system["Debug"]!["MaxQueuedFrames"]!.Value<int>(), Is.EqualTo(3));
            Assert.That(system["Rendering"]!["MainScreenScale"]!.Value<float>(), Is.EqualTo(0.8f));
            Assert.That(MajEnv.UserSettings.Game.TapSpeed, Is.EqualTo(8.5f));
            Assert.That(MajEnv.UserSettings.Display.BGInfo, Is.EqualTo(BGInfoOption.None));
            Assert.That(MajEnv.UserSettings.Volume.Global, Is.EqualTo(0.9f));
            Assert.That(MajEnv.UserSettings.Judge.OffsetUnit, Is.EqualTo(OffsetUnitOption.Second));

            Step("OnSave", null, null);

            var user = JObject.Parse(_root.FindFile("settings.json")!.ReadAllText());
            Assert.That(user.Properties().Select(category => category.Name), Is.EqualTo(new[] { "Game", "Judge", "Display", "Volume", "Touch" }));
            Assert.That(user["Display"]!["BGInfo"]!.Value<string>(), Is.EqualTo("None"));
        }

        /// <summary>Checks that unreadable settings are replaced by defaults and backed up without overwriting older backups.</summary>
        [Test]
        public void InitSettings_BacksUpCorruptFile()
        {
            _root.CreateFile("settings.json").WriteAllText("{ broken");
            _root.CreateFile("settings.json.bak").WriteAllText("older");

            Step("InitSettings");

            Assert.That(MajEnv.UserSettings.Game.TapSpeed, Is.EqualTo(7.5f));
            Assert.That(_root.FindFile("settings.json.bak")!.ReadAllText(), Is.EqualTo("older"));
            Assert.That(_root.FindFile("settings.json.bak.bak")!.ReadAllText(), Is.EqualTo("{ broken"));
        }

        /// <summary>Checks that an unreadable system-settings.json is backed up and replaced by defaults while settings.json is kept.</summary>
        [Test]
        public void InitSettings_BacksUpCorruptSystemFileOnly()
        {
            _root.CreateFile("settings.json").WriteAllText("{\"Game\":{\"TapSpeed\":8.5}}");
            _root.CreateFile("system-settings.json").WriteAllText("{\"Debug\":{\"MaxQueuedFrames\":\"many\"}}");

            Step("InitSettings");

            Assert.That(MajEnv.UserSettings.Game.TapSpeed, Is.EqualTo(8.5f));
            Assert.That(MajEnv.SystemSettings.Debug.MaxQueuedFrames, Is.EqualTo(2));
            Assert.That(_root.FindFile("settings.json.bak"), Is.Null);
            Assert.That(_root.FindFile("system-settings.json.bak")!.ReadAllText(), Is.EqualTo("{\"Debug\":{\"MaxQueuedFrames\":\"many\"}}"));
        }

        /// <summary>
        /// Checks that legacy system settings that cannot be read back up settings.json, their source, while
        /// system-settings.json is created with defaults and the user settings are kept.
        /// </summary>
        [Test]
        public void InitSettings_BacksUpLegacySourceOfUnreadableSystemSettings()
        {
            const string legacy = "{\"Game\":{\"TapSpeed\":8.5},\"Debug\":{\"MaxQueuedFrames\":\"many\"}}";
            _root.CreateFile("settings.json").WriteAllText(legacy);

            Step("InitSettings");

            Assert.That(MajEnv.UserSettings.Game.TapSpeed, Is.EqualTo(8.5f));
            Assert.That(MajEnv.SystemSettings.Debug.MaxQueuedFrames, Is.EqualTo(2));
            Assert.That(_root.FindFile("settings.json.bak")!.ReadAllText(), Is.EqualTo(legacy));
            Assert.That(JObject.Parse(_root.FindFile("system-settings.json")!.ReadAllText())["Debug"]!["MaxQueuedFrames"]!.Value<int>(), Is.EqualTo(2));
        }

        /// <summary>Checks that a legacy value that cannot be upgraded backs up settings.json and keeps system-settings.json.</summary>
        [Test]
        public void InitSettings_UnreadableLegacyValueKeepsSystemSettings()
        {
            const string legacy = "{\"Game\":{\"TapSpeed\":8.5},\"Display\":{\"RenderQuality\":\"Bogus\"},\"Debug\":{\"MaxQueuedFrames\":3}}";
            _root.CreateFile("settings.json").WriteAllText(legacy);
            _root.CreateFile("system-settings.json").WriteAllText("{\"Debug\":{\"MaxQueuedFrames\":4}}");

            Step("InitSettings");

            Assert.That(MajEnv.UserSettings.Game.TapSpeed, Is.EqualTo(7.5f));
            Assert.That(MajEnv.SystemSettings.Debug.MaxQueuedFrames, Is.EqualTo(4));
            Assert.That(_root.FindFile("settings.json.bak")!.ReadAllText(), Is.EqualTo(legacy));
            Assert.That(_root.FindFile("system-settings.json.bak"), Is.Null);
        }

        /// <summary>Opens runtime.db with a separate connection.</summary>
        /// <returns>The open connection, which the caller disposes.</returns>
        private static SQLiteConnection OpenStoredRuntimeDatabase()
        {
            var file = MajEnv.RuntimeDirectory.FindFile("runtime.db")!;
            return new SQLiteConnection(file.Location, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
        }

        /// <summary>Reads one column of the stored list configuration row as text.</summary>
        /// <param name="column">The column name.</param>
        /// <returns>The stored text, or null when the row or value is absent.</returns>
        private static string? ReadStoredListConfig(string column)
        {
            using var connection = OpenStoredRuntimeDatabase();
            return connection.ExecuteScalar<string?>($"SELECT CAST({column} AS TEXT) FROM ListConfig WHERE Id = 0");
        }

        /// <summary>Counts the stored list configuration rows.</summary>
        /// <returns>The number of rows.</returns>
        private static int CountStoredListConfigRows()
        {
            using var connection = OpenStoredRuntimeDatabase();
            return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM ListConfig");
        }

        /// <summary>Checks that the runtime configuration is created in typed runtime.db columns, loaded and reset when unreadable.</summary>
        [Test]
        public void InitRuntimeConfig_CreatesLoadsAndRecovers()
        {
            Step("InitRuntimeConfig");
            Assert.That(CountStoredListConfigRows(), Is.EqualTo(1));
            Assert.That(MajEnv.RuntimeDirectory.FindFile("config.json"), Is.Null);
            Assert.That(MajEnv.RuntimeConfig, Is.Not.Null);

            var selectedId = System.Guid.Parse("4d2783ac-a6ef-40f4-b648-b043b4c58608");
            using (var connection = OpenStoredRuntimeDatabase())
            {
                connection.Execute(
                    "UPDATE ListConfig SET SelectedDirGuid = ?, SelectedSongId = ?, SelectedDiff = ? WHERE Id = 0",
                    selectedId.ToString(), "h", (int)ChartLevel.Master);
            }
            Step("InitRuntimeConfig");
            Assert.That(MajEnv.RuntimeConfig!.List.SelectedDirGuid, Is.EqualTo(selectedId));
            Assert.That(MajEnv.RuntimeConfig.List.SelectedSongId, Is.EqualTo("h"));
            Assert.That(MajEnv.RuntimeConfig.List.SelectedDiff, Is.EqualTo(ChartLevel.Master));

            using (var connection = OpenStoredRuntimeDatabase())
            {
                connection.Execute("UPDATE ListConfig SET SelectedDirGuid = 'not a guid', SelectedSongId = NULL WHERE Id = 0");
            }
            Step("InitRuntimeConfig");
            Assert.That(MajEnv.RuntimeConfig!.List.SelectedDirGuid, Is.EqualTo(System.Guid.Empty));
            Assert.That(MajEnv.RuntimeConfig.List.SelectedSongId, Is.Empty);
            Assert.That(MajEnv.RuntimeConfig.List.SelectedDiff, Is.EqualTo(ChartLevel.Easy));
        }

        /// <summary>Checks that a NULL text column, as left by a column added later, loads as an empty value.</summary>
        [Test]
        public void InitRuntimeConfig_LoadsNullSongIdAsEmpty()
        {
            Step("InitRuntimeConfig");
            using (var connection = OpenStoredRuntimeDatabase())
            {
                connection.Execute("UPDATE ListConfig SET SelectedSongId = NULL WHERE Id = 0");
            }

            Step("InitRuntimeConfig");

            Assert.That(MajEnv.RuntimeConfig.List.SelectedSongId, Is.Empty);
        }

        /// <summary>Checks that a legacy config.json is imported into runtime.db once and then backed up.</summary>
        [Test]
        public void InitRuntimeConfig_ImportsLegacyJson()
        {
            MajEnv.RuntimeDirectory.CreateFile("config.json").WriteAllText("{\"List\":{\"SelectedSongId\":\"legacy\"}}");
            MajEnv.RuntimeDirectory.CreateFile("config.json.bak").WriteAllText("older");

            Step("InitRuntimeConfig");

            Assert.That(MajEnv.RuntimeConfig.List.SelectedSongId, Is.EqualTo("legacy"));
            Assert.That(ReadStoredListConfig("SelectedSongId"), Is.EqualTo("legacy"));
            Assert.That(MajEnv.RuntimeDirectory.FindFile("config.json"), Is.Null);
            Assert.That(MajEnv.RuntimeDirectory.FindFile("config.json.bak")!.ReadAllText(), Does.Contain("legacy"));

            // A config.json appearing after the import is ignored, because runtime.db is the only source from then on.
            MajEnv.RuntimeDirectory.CreateFile("config.json").WriteAllText("{\"List\":{\"SelectedSongId\":\"stale\"}}");
            Step("InitRuntimeConfig");
            Assert.That(MajEnv.RuntimeConfig.List.SelectedSongId, Is.EqualTo("legacy"));
        }

        /// <summary>Checks that an unreadable legacy config.json is imported as defaults and kept as a backup.</summary>
        [Test]
        public void InitRuntimeConfig_ImportsUnreadableLegacyJsonAsDefaults()
        {
            MajEnv.RuntimeDirectory.CreateFile("config.json").WriteAllText("not json");

            Step("InitRuntimeConfig");

            Assert.That(MajEnv.RuntimeConfig.List.SelectedSongId, Is.Empty);
            Assert.That(CountStoredListConfigRows(), Is.EqualTo(1));
            Assert.That(MajEnv.RuntimeDirectory.FindFile("config.json.bak")!.ReadAllText(), Is.EqualTo("not json"));
        }

        /// <summary>
        /// Checks that a legacy machine_description.json is imported into the system settings, written to
        /// system-settings.json and backed up, and that the machine description system-settings.json then holds wins.
        /// </summary>
        [Test]
        public void InitSettings_ImportsLegacyMachineDescription()
        {
            _root.CreateFile("machine_description.json").WriteAllText("{\"Name\":\"Cabinet\",\"Description\":\"Corner\"}");

            Step("InitSettings");

            var machine = MajEnv.SystemSettings.Network.Online.Machine;
            var system = JObject.Parse(_root.FindFile("system-settings.json")!.ReadAllText());
            Assert.That(machine.Name, Is.EqualTo("Cabinet"));
            Assert.That(machine.Description, Is.EqualTo("Corner"));
            Assert.That(system["Network"]!["Online"]!["Machine"]!["Name"]!.Value<string>(), Is.EqualTo("Cabinet"));
            Assert.That(_root.FindFile("machine_description.json"), Is.Null);
            Assert.That(_root.FindFile("machine_description.json.bak")!.ReadAllText(), Does.Contain("Cabinet"));

            _root.CreateFile("machine_description.json").WriteAllText("{\"Name\":\"Ignored\"}");
            Step("InitSettings");

            Assert.That(MajEnv.SystemSettings.Network.Online.Machine.Name, Is.EqualTo("Cabinet"));
            Assert.That(_root.FindFile("machine_description.json"), Is.Not.Null);
        }

        /// <summary>Checks that an unreadable legacy machine_description.json keeps the default machine description and stays in place.</summary>
        [Test]
        public void InitSettings_UnreadableMachineDescriptionKeepsDefaults()
        {
            _root.CreateFile("machine_description.json").WriteAllText("[broken");

            Step("InitSettings");

            Assert.That(MajEnv.SystemSettings.Network.Online.Machine.Name, Is.EqualTo("MajdataPlay Client"));
            Assert.That(_root.FindFile("machine_description.json")!.ReadAllText(), Is.EqualTo("[broken"));
            Assert.That(_root.FindFile("machine_description.json.bak"), Is.Null);
        }

        /// <summary>Checks that endpoints get a trailing slash, keep their credentials and are deduplicated by URL.</summary>
        [Test]
        public void InitOnlineEndpoints_NormalizesEndpoints()
        {
            MajEnv.SystemSettings.Network.Online.ApiEndpoints = new[]
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
            var options = new ProxyOptions { Enable = useProxy, Url = proxy };
            PrivateMember.SetProperty(MajEnv.SystemSettings.Network, nameof(NetworkOptions.Proxy), options);
        }

        /// <summary>Checks that saving writes the user and system settings and runtime configuration, recreating deleted files.</summary>
        [Test]
        public void OnSave_WritesConfiguration()
        {
            _environment.Set(nameof(MajEnv.RuntimeConfig), new RuntimeConfig());
            MajEnv.UserSettings.Game.TapSpeed = 6.25f;
            MajEnv.SystemSettings.Rendering.FPSLimit = 90;
            MajEnv.RuntimeConfig!.List.SelectedSongId = "saved";

            Step("OnSave", null, null);

            var user = Serializer.Json.Deserialize<UserSetting>(_root.FindFile("settings.json")!.ReadAllText(), MajEnv.UserJsonReaderOption)!;
            var system = Serializer.Json.Deserialize<SystemSetting>(_root.FindFile("system-settings.json")!.ReadAllText(), MajEnv.UserJsonReaderOption)!;
            Assert.That(user.Game.TapSpeed, Is.EqualTo(6.25f));
            Assert.That(system.Rendering.FPSLimit, Is.EqualTo(90));
            Assert.That(ReadStoredListConfig("SelectedSongId"), Is.EqualTo("saved"));
            Assert.That(MajEnv.UserSettingsPath.Location, Is.EqualTo(_root.FindFile("settings.json")!.Location));
            Assert.That(MajEnv.SystemSettingsPath.Location, Is.EqualTo(_root.FindFile("system-settings.json")!.Location));

            MajEnv.UserSettings.Game.TapSpeed = 5f;
            Step("OnSave", null, null);
            var shorter = _root.FindFile("settings.json")!.ReadAllText();
            Assert.That(Serializer.Json.Deserialize<UserSetting>(shorter, MajEnv.UserJsonReaderOption)!.Game.TapSpeed, Is.EqualTo(5f));
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
