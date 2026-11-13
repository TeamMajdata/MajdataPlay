using MajdataPlay.IO.Storage;
using MajdataPlay.Net;
using MajdataPlay.Settings;
using MajdataPlay.Settings.Sync;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Misc.Support;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Settings
{
    /// <summary>
    /// Covers which copy of the user settings the game uses: the local settings.json while no account owns them, and the
    /// copy of the account after a login.
    /// </summary>
    public class UserSettingSyncTests
    {
        /// <summary>The temporary storage.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>The scope restoring the static environment.</summary>
        private MajEnvScope _environment = null!;

        /// <summary>The application root inside the temporary storage.</summary>
        private StorageDirectory _root = null!;

        /// <summary>The endpoint that stores user settings.</summary>
        private ApiEndpoint _primary = null!;

        /// <summary>The answers the tested code reads instead of a server.</summary>
        private FakeUserSettingApi _api = null!;

        /// <summary>Points the environment at a temporary layout and replaces the settings server.</summary>
        [SetUp]
        public void SetUp()
        {
            _temporary = new TemporaryDirectory();
            _environment = new MajEnvScope();
            _root = _environment.UseTemporaryLayout(_temporary);
            _primary = new ApiEndpoint
            {
                Name = "primary",
                Url = new Uri("https://primary.invalid/api/"),
            };
            _environment.Set(nameof(MajEnv.ApiEndpoints), new[] { _primary });
            _api = new FakeUserSettingApi();
            UserSettingSync.Api = _api;
            UserSettingSync.PushDebounceMilliseconds = 0;
        }

        /// <summary>Restores the environment and the source of the settings.</summary>
        [TearDown]
        public void TearDown()
        {
            MajEnv.SetUserSettingSource(UserSettingSource.Local, null);
            UserSettingSync.Reset();
            _environment.Dispose();
            _temporary.Dispose();
        }

        /// <summary>Checks that the copy of the account replaces the local settings as soon as the user logs in.</summary>
        [Test]
        public void Attach_UsesTheAccountCopy()
        {
            InitLocalSettings(8.5f);
            _api.FetchResult = "{\"Game\":{\"TapSpeed\":9.5}}";

            var result = Attach(_primary);

            Assert.That(result, Is.EqualTo(UserSettingAttachResult.Attached));
            Assert.That(MajEnv.UserSettings.Game.TapSpeed, Is.EqualTo(9.5f));
            Assert.That(MajEnv.ActiveUserSettingSource, Is.EqualTo(UserSettingSource.Remote));
            Assert.That(MajEnv.UserSettingOwner, Is.SameAs(_primary));
        }

        /// <summary>Checks that an account without settings receives the local settings, which then become its copy.</summary>
        [Test]
        public void Attach_WhenTheAccountIsEmpty_UploadsTheLocalCopy()
        {
            InitLocalSettings(8.5f);
            _api.FetchResult = "{}";

            var result = Attach(_primary);

            Assert.That(result, Is.EqualTo(UserSettingAttachResult.Repaired));
            Assert.That(_api.StoreCount, Is.EqualTo(1));
            Assert.That(MajEnv.ActiveUserSettingSource, Is.EqualTo(UserSettingSource.Remote));
            var stored = JObject.Parse(_api.LastStoredJson!);
            Assert.That(stored.Properties().Select(property => property.Name),
                Is.EqualTo(new[] { "Game", "Judge", "Display", "Volume", "Touch" }));
            Assert.That(stored["Game"]!["TapSpeed"]!.Value<float>(), Is.EqualTo(8.5f));
            // The device settings, including the credentials of the endpoints, never reach an account.
            Assert.That(stored.ContainsKey("Online"), Is.False);
            Assert.That(stored.ContainsKey("IO"), Is.False);
            Assert.That(stored.ContainsKey("Audio"), Is.False);
            Assert.That(stored.ContainsKey("Debug"), Is.False);
        }

        /// <summary>Checks that an unreadable account copy is replaced by the local settings instead of stopping the login.</summary>
        [Test]
        public void Attach_WhenTheAccountCopyIsUnreadable_UploadsTheLocalCopy()
        {
            InitLocalSettings(8.5f);
            _api.FetchResult = "{ broken";

            var result = Attach(_primary);

            Assert.That(result, Is.EqualTo(UserSettingAttachResult.Repaired));
            Assert.That(_api.StoreCount, Is.EqualTo(1));
            Assert.That(MajEnv.UserSettings.Game.TapSpeed, Is.EqualTo(8.5f));
        }

        /// <summary>Checks that a failed fetch falls back to the local settings for the session.</summary>
        [Test]
        public void Attach_WhenTheFetchFails_KeepsTheLocalCopy()
        {
            InitLocalSettings(8.5f);
            _api.FetchResult = null;

            var result = Attach(_primary);

            Assert.That(result, Is.EqualTo(UserSettingAttachResult.Degraded));
            Assert.That(MajEnv.UserSettings.Game.TapSpeed, Is.EqualTo(8.5f));
            Assert.That(MajEnv.ActiveUserSettingSource, Is.EqualTo(UserSettingSource.Local));
        }

        /// <summary>Checks that only the first configured endpoint exchanges user settings.</summary>
        [Test]
        public void Attach_UsesOnlyTheFirstEndpoint()
        {
            InitLocalSettings(8.5f);
            var mirror = new ApiEndpoint
            {
                Name = "mirror",
                Url = new Uri("https://mirror.invalid/api/"),
            };
            _environment.Set(nameof(MajEnv.ApiEndpoints), new[] { _primary, mirror });

            var result = Attach(mirror);

            Assert.That(result, Is.EqualTo(UserSettingAttachResult.Skipped));
            Assert.That(_api.FetchCount, Is.Zero);
            Assert.That(MajEnv.ActiveUserSettingSource, Is.EqualTo(UserSettingSource.Local));
        }

        /// <summary>Checks that a save uploads while the account owns the settings and leaves settings.json untouched.</summary>
        [Test]
        public void Save_WhileTheAccountOwnsTheSettings_UploadsAndKeepsTheLocalFile()
        {
            InitLocalSettings(8.5f);
            _api.FetchResult = "{\"Game\":{\"TapSpeed\":9.5}}";
            Assert.That(Attach(_primary), Is.EqualTo(UserSettingAttachResult.Attached));
            var localBeforeSave = _root.FindFile("settings.json")!.ReadAllText();
            _api.FetchResult = null;

            MajEnv.UserSettings.Game.TapSpeed = 6.5f;
            Step("OnSave", null, null);
            WaitUntil(() => _api.StoreCount > 0);

            Assert.That(_root.FindFile("settings.json")!.ReadAllText(), Is.EqualTo(localBeforeSave));
            var stored = JObject.Parse(_api.LastStoredJson!);
            Assert.That(stored["Game"]!["TapSpeed"]!.Value<float>(), Is.EqualTo(6.5f));
        }

        /// <summary>Checks that a save without an account still writes settings.json.</summary>
        [Test]
        public void Save_WithoutAnAccount_WritesTheLocalFile()
        {
            InitLocalSettings(8.5f);

            MajEnv.UserSettings.Game.TapSpeed = 6.5f;
            Step("OnSave", null, null);

            var saved = JObject.Parse(_root.FindFile("settings.json")!.ReadAllText());
            Assert.That(saved["Game"]!["TapSpeed"]!.Value<float>(), Is.EqualTo(6.5f));
            Assert.That(_api.StoreCount, Is.Zero);
        }

        /// <summary>Checks that leaving the account brings back the settings of the last local session.</summary>
        [Test]
        public void Detach_RestoresTheLocalCopy()
        {
            InitLocalSettings(8.5f);
            _api.FetchResult = "{\"Game\":{\"TapSpeed\":9.5}}";
            Assert.That(Attach(_primary), Is.EqualTo(UserSettingAttachResult.Attached));

            UserSettingSync.DetachAsync().AsTask().GetAwaiter().GetResult();

            Assert.That(MajEnv.UserSettings.Game.TapSpeed, Is.EqualTo(8.5f));
            Assert.That(MajEnv.ActiveUserSettingSource, Is.EqualTo(UserSettingSource.Local));
            Assert.That(MajEnv.UserSettingOwner, Is.Null);
        }

        /// <summary>Checks which documents of an account are read as settings, as empty, or as unreadable.</summary>
        [Test]
        public void ReadUserSettings_TellsEmptyAndUnreadableDocumentsApart()
        {
            Assert.That(MajEnv.ReadUserSettings("{}", out var empty), Is.EqualTo(UserSettingReadResult.Empty));
            Assert.That(empty, Is.Null);
            Assert.That(MajEnv.ReadUserSettings(string.Empty, out _), Is.EqualTo(UserSettingReadResult.Empty));
            Assert.That(MajEnv.ReadUserSettings("null", out _), Is.EqualTo(UserSettingReadResult.Empty));
            Assert.That(MajEnv.ReadUserSettings("{ broken", out _), Is.EqualTo(UserSettingReadResult.Invalid));
            Assert.That(MajEnv.ReadUserSettings("{\"Game\":{\"TapSpeed\":9}}", out var settings),
                Is.EqualTo(UserSettingReadResult.Ok));
            Assert.That(settings!.Game.TapSpeed, Is.EqualTo(9f));
        }

        /// <summary>Checks that a value out of range is corrected whether it came from the account or from settings.json.</summary>
        [Test]
        public void Attach_CorrectsValuesOutOfRange()
        {
            InitLocalSettings(8.5f);
            _api.FetchResult = "{\"Judge\":{\"InnerJudgeDistance\":4,\"OuterJudgeDistance\":-1}}";

            Assert.That(Attach(_primary), Is.EqualTo(UserSettingAttachResult.Attached));

            Assert.That(MajEnv.UserSettings.Judge.InnerJudgeDistance, Is.EqualTo(1f));
            Assert.That(MajEnv.UserSettings.Judge.OuterJudgeDistance, Is.EqualTo(0f));
        }

        /// <summary>Writes the settings of a local session and loads them the way the game starts.</summary>
        /// <param name="tapSpeed">The tap speed the local settings hold.</param>
        private void InitLocalSettings(float tapSpeed)
        {
            _root.CreateFile("settings.json").WriteAllText(
                $"{{\"Game\":{{\"TapSpeed\":{tapSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}}}");
            Step("InitSettings");
        }

        /// <summary>Attaches the settings to an account.</summary>
        /// <param name="endpoint">The endpoint the user logged in on.</param>
        /// <returns>The outcome of the attach.</returns>
        private static UserSettingAttachResult Attach(ApiEndpoint endpoint)
        {
            return UserSettingSync.AttachAsync(endpoint).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>Waits until an assertion condition holds, so a background upload can finish.</summary>
        /// <param name="condition">The condition to wait for.</param>
        private static void WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }
                Thread.Sleep(10);
            }
            Assert.Fail("The expected background work did not finish in time.");
        }

        /// <summary>Invokes a private member of <see cref="MajEnv"/>.</summary>
        /// <param name="name">The member name.</param>
        /// <param name="arguments">The arguments.</param>
        private static void Step(string name, params object?[] arguments)
        {
            PrivateMember.InvokeStatic(typeof(MajEnv), name, arguments);
        }

        /// <summary>Answers the settings exchange from memory instead of a server.</summary>
        private sealed class FakeUserSettingApi : IUserSettingApi
        {
            /// <summary>Gets or sets the document the next fetch returns, where null reports a failed request.</summary>
            public string? FetchResult { get; set; }

            /// <summary>Gets the number of fetches.</summary>
            public int FetchCount { get; private set; }

            /// <summary>Gets the number of stores.</summary>
            public int StoreCount { get; private set; }

            /// <summary>Gets the document of the last store.</summary>
            public string? LastStoredJson { get; private set; }

            /// <inheritdoc />
            public ValueTask<string?> FetchAsync(ApiEndpoint endpoint, CancellationToken token)
            {
                FetchCount++;
                return new ValueTask<string?>(FetchResult);
            }

            /// <inheritdoc />
            public ValueTask<bool> StoreAsync(ApiEndpoint endpoint, string json, CancellationToken token)
            {
                StoreCount++;
                LastStoredJson = json;
                return new ValueTask<bool>(true);
            }
        }
    }
}
