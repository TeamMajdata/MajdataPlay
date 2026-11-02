using MajdataPlay.Diagnostics;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Settings
{
    /// <summary>
    /// Covers platform JSON exclusions, default contracts and their integration with the settings serializer.
    /// </summary>
    public class SettingContractResolverTests
    {
        /// <summary>A settings object with property, field, group and renamed-member exclusions.</summary>
        public sealed class PlatformProbe
        {
            /// <summary>Gets or sets an option excluded on mobile targets.</summary>
            [JsonIgnoreOnPlatform(SettingPlatform.Mobile)]
            public int DesktopValue { get; set; } = 1;

            /// <summary>An option excluded on desktop targets.</summary>
            [JsonIgnoreOnPlatform(SettingPlatform.Desktop)]
            public int MobileField = 2;

            /// <summary>Gets or sets an option excluded only on iOS.</summary>
            [JsonIgnoreOnPlatform(SettingPlatform.IOS)]
            public int IOSValue { get; set; } = 3;

            /// <summary>Gets or sets an option excluded on every supported target.</summary>
            [JsonIgnoreOnPlatform(SettingPlatform.All)]
            public int AllValue { get; set; } = 4;

            /// <summary>Gets or sets an option whose empty exclusion mask never matches.</summary>
            [JsonIgnoreOnPlatform(SettingPlatform.None)]
            public int NoneValue { get; set; } = 5;

            /// <summary>Gets or sets an option whose unconditional ignore must always take precedence.</summary>
            [JsonIgnore]
            [JsonIgnoreOnPlatform(SettingPlatform.Android)]
            public int IgnoredValue { get; set; } = 6;

            /// <summary>Gets or sets a renamed option excluded on every supported target except Windows.</summary>
            [JsonProperty("windowsOnly")]
            [JsonIgnoreOnPlatform(SettingPlatform.All & ~SettingPlatform.Windows)]
            public int WindowsValue { get; set; } = 7;

            /// <summary>Gets or sets an option without any exclusion.</summary>
            public int NormalValue { get; set; } = 8;
        }

        /// <summary>A base settings type that declares an inheritable platform exclusion.</summary>
        public class BaseProbe
        {
            /// <summary>Gets or sets an option whose exclusion is inherited by overrides.</summary>
            [JsonIgnoreOnPlatform(SettingPlatform.IOS)]
            public virtual int Value { get; set; } = 1;
        }

        /// <summary>A settings type that overrides an option without repeating its exclusion.</summary>
        public sealed class DerivedProbe : BaseProbe
        {
            /// <inheritdoc/>
            public override int Value { get; set; } = 2;
        }

        /// <summary>Creates the production settings serializer with a separate resolver for the requested platform.</summary>
        /// <param name="platform">The target platform whose exclusions apply.</param>
        /// <returns>A serializer with the production settings and a fixed platform contract cache.</returns>
        private static JsonSerializer CreateSerializer(SettingPlatform platform)
        {
            var serializer = JsonSerializer.CreateDefault(MajEnv.UserJsonReaderOption);
            serializer.ContractResolver = new SettingContractResolver(platform);
            return serializer;
        }

        /// <summary>Checks that fields and properties are excluded only on matching individual or grouped platforms.</summary>
        /// <param name="platform">The platform to evaluate.</param>
        /// <param name="desktopIgnored">Whether the desktop option is excluded.</param>
        /// <param name="mobileIgnored">Whether the mobile field is excluded.</param>
        /// <param name="iosIgnored">Whether the iOS-specific option is excluded.</param>
        [TestCase(SettingPlatform.Windows, false, true, false)]
        [TestCase(SettingPlatform.Linux, false, true, false)]
        [TestCase(SettingPlatform.MacOS, false, true, false)]
        [TestCase(SettingPlatform.Android, true, false, false)]
        [TestCase(SettingPlatform.IOS, true, false, true)]
        [TestCase(SettingPlatform.None, false, false, false)]
        public void Serialize_ExcludesMatchingMembers(SettingPlatform platform, bool desktopIgnored, bool mobileIgnored, bool iosIgnored)
        {
            var json = JObject.FromObject(new PlatformProbe(), CreateSerializer(platform));

            Assert.That(json.ContainsKey(nameof(PlatformProbe.DesktopValue)), Is.EqualTo(!desktopIgnored));
            Assert.That(json.ContainsKey(nameof(PlatformProbe.MobileField)), Is.EqualTo(!mobileIgnored));
            Assert.That(json.ContainsKey(nameof(PlatformProbe.IOSValue)), Is.EqualTo(!iosIgnored));
            Assert.That(json.ContainsKey(nameof(PlatformProbe.AllValue)), Is.EqualTo(platform == SettingPlatform.None));
            Assert.That(json[nameof(PlatformProbe.NoneValue)]!.Value<int>(), Is.EqualTo(5));
            Assert.That(json.ContainsKey(nameof(PlatformProbe.IgnoredValue)), Is.False);
            Assert.That(json[nameof(PlatformProbe.NormalValue)]!.Value<int>(), Is.EqualTo(8));
        }

        /// <summary>Checks that matching JSON members retain their defaults while other members are read normally.</summary>
        /// <param name="platform">The platform to evaluate.</param>
        /// <param name="desktopIgnored">Whether the desktop option is excluded.</param>
        /// <param name="mobileIgnored">Whether the mobile field is excluded.</param>
        /// <param name="iosIgnored">Whether the iOS-specific option is excluded.</param>
        [TestCase(SettingPlatform.Windows, false, true, false)]
        [TestCase(SettingPlatform.Linux, false, true, false)]
        [TestCase(SettingPlatform.MacOS, false, true, false)]
        [TestCase(SettingPlatform.Android, true, false, false)]
        [TestCase(SettingPlatform.IOS, true, false, true)]
        [TestCase(SettingPlatform.None, false, false, false)]
        public void Deserialize_LeavesMatchingMembersAtDefaults(SettingPlatform platform, bool desktopIgnored, bool mobileIgnored, bool iosIgnored)
        {
            var json = JObject.Parse("{\"DesktopValue\":99,\"MobileField\":99,\"IOSValue\":99,\"AllValue\":99," +
                                     "\"NoneValue\":99,\"IgnoredValue\":99,\"NormalValue\":99}");
            var restored = json.ToObject<PlatformProbe>(CreateSerializer(platform))!;

            Assert.That(restored.DesktopValue, Is.EqualTo(desktopIgnored ? 1 : 99));
            Assert.That(restored.MobileField, Is.EqualTo(mobileIgnored ? 2 : 99));
            Assert.That(restored.IOSValue, Is.EqualTo(iosIgnored ? 3 : 99));
            Assert.That(restored.AllValue, Is.EqualTo(platform == SettingPlatform.None ? 99 : 4));
            Assert.That(restored.NoneValue, Is.EqualTo(99));
            Assert.That(restored.IgnoredValue, Is.EqualTo(6));
            Assert.That(restored.NormalValue, Is.EqualTo(99));
        }

        /// <summary>Checks exclusion masks and preservation of explicit JSON property names.</summary>
        /// <param name="platform">The platform to evaluate.</param>
        /// <param name="ignored">Whether the renamed option is excluded.</param>
        [TestCase(SettingPlatform.Windows, false)]
        [TestCase(SettingPlatform.Linux, true)]
        [TestCase(SettingPlatform.MacOS, true)]
        [TestCase(SettingPlatform.Android, true)]
        [TestCase(SettingPlatform.IOS, true)]
        [TestCase(SettingPlatform.None, false)]
        public void Json_RespectsExclusionMasksAndRenamedMembers(SettingPlatform platform, bool ignored)
        {
            var serializer = CreateSerializer(platform);
            var json = JObject.FromObject(new PlatformProbe(), serializer);
            var restored = JObject.Parse("{\"windowsOnly\":99}").ToObject<PlatformProbe>(serializer)!;

            Assert.That(json.ContainsKey("windowsOnly"), Is.EqualTo(!ignored));
            Assert.That(json.ContainsKey(nameof(PlatformProbe.WindowsValue)), Is.False);
            Assert.That(restored.WindowsValue, Is.EqualTo(ignored ? 7 : 99));
        }

        /// <summary>Checks that an excluded member is skipped before its JSON value is converted.</summary>
        [Test]
        public void Deserialize_IgnoresInvalidValuesOfExcludedMembers()
        {
            var serializer = CreateSerializer(SettingPlatform.IOS);
            var json = JObject.Parse("{\"DesktopValue\":{},\"IOSValue\":[],\"AllValue\":\"invalid\",\"IgnoredValue\":null}");
            var restored = json.ToObject<PlatformProbe>(serializer)!;

            Assert.That(restored.DesktopValue, Is.EqualTo(1));
            Assert.That(restored.IOSValue, Is.EqualTo(3));
            Assert.That(restored.AllValue, Is.EqualTo(4));
            Assert.That(restored.IgnoredValue, Is.EqualTo(6));
            Assert.That(() => json.ToObject<PlatformProbe>(CreateSerializer(SettingPlatform.Windows)), Throws.InstanceOf<JsonException>());
        }

        /// <summary>Checks that populating an existing object leaves excluded values untouched.</summary>
        [Test]
        public void Populate_PreservesExistingExcludedValues()
        {
            var probe = new PlatformProbe { IOSValue = 42 };
            using var reader = JObject.Parse("{\"IOSValue\":99,\"NormalValue\":99}").CreateReader();

            CreateSerializer(SettingPlatform.IOS).Populate(reader, probe);

            Assert.That(probe.IOSValue, Is.EqualTo(42));
            Assert.That(probe.NormalValue, Is.EqualTo(99));
        }

        /// <summary>Checks inherited exclusions on overridden properties.</summary>
        /// <param name="platform">The platform to evaluate.</param>
        /// <param name="ignored">Whether the inherited exclusion applies.</param>
        [TestCase(SettingPlatform.IOS, true)]
        [TestCase(SettingPlatform.Windows, false)]
        public void Json_InheritsExclusions(SettingPlatform platform, bool ignored)
        {
            var serializer = CreateSerializer(platform);
            var json = JObject.FromObject(new DerivedProbe(), serializer);
            var restored = JObject.Parse("{\"Value\":99}").ToObject<DerivedProbe>(serializer)!;

            Assert.That(json.ContainsKey(nameof(DerivedProbe.Value)), Is.EqualTo(!ignored));
            Assert.That(restored.Value, Is.EqualTo(ignored ? 2 : 99));
        }

        /// <summary>Checks that resolving the same type for different platforms cannot share excluded contracts.</summary>
        [Test]
        public void ResolveContract_CachesRemainIndependentBetweenPlatforms()
        {
            var ios = CreateSerializer(SettingPlatform.IOS);
            var windows = CreateSerializer(SettingPlatform.Windows);
            var probe = new PlatformProbe();

            for (var i = 0; i < 2; i++)
            {
                Assert.That(JObject.FromObject(probe, ios).ContainsKey(nameof(PlatformProbe.IOSValue)), Is.False);
                Assert.That(JObject.FromObject(probe, windows).ContainsKey(nameof(PlatformProbe.IOSValue)), Is.True);
            }
        }

        /// <summary>Checks the three migrated production options on each supported platform.</summary>
        /// <param name="platform">The platform to evaluate.</param>
        /// <param name="ignored">Whether the original iOS-only ignores apply.</param>
        [TestCase(SettingPlatform.Windows, false)]
        [TestCase(SettingPlatform.Linux, false)]
        [TestCase(SettingPlatform.MacOS, false)]
        [TestCase(SettingPlatform.Android, false)]
        [TestCase(SettingPlatform.IOS, true)]
        public void Json_ProductionOptionsPreservePlatformBehavior(SettingPlatform platform, bool ignored)
        {
            var serializer = CreateSerializer(platform);
            var debug = JObject.FromObject(new DebugOptions(), serializer);
            var online = JObject.FromObject(new OnlineOptions(), serializer);
            var restoredDebug = JObject.Parse("{\"NoteFolding\":false,\"DebugLevel\":\"Error\"}").ToObject<DebugOptions>(serializer)!;
            var restoredOnline = JObject.Parse("{\"Enable\":true}").ToObject<OnlineOptions>(serializer)!;

            Assert.That(debug.ContainsKey(nameof(DebugOptions.NoteFolding)), Is.EqualTo(!ignored));
            Assert.That(debug.ContainsKey(nameof(DebugOptions.DebugLevel)), Is.EqualTo(!ignored));
            Assert.That(online.ContainsKey(nameof(OnlineOptions.Enable)), Is.EqualTo(!ignored));
            Assert.That(restoredDebug.NoteFolding, Is.EqualTo(ignored));
            Assert.That(restoredDebug.DebugLevel, Is.EqualTo(ignored ? LogLevel.Info : LogLevel.Error));
            Assert.That(restoredOnline.Enable, Is.EqualTo(!ignored));
        }

        /// <summary>Checks that the shared production settings configuration applies the current build target.</summary>
        [Test]
        public void SettingsSerializer_UsesCurrentBuildTargetResolver()
        {
            Assert.That(MajEnv.UserJsonReaderOption.ContractResolver, Is.InstanceOf<SettingContractResolver>());
            var json = JObject.Parse(Serializer.Json.Serialize(new DebugOptions(), MajEnv.UserJsonReaderOption));
            var (_, system) = MajEnv.DeserializeSettings("{}", "{\"Debug\":{\"NoteFolding\":false,\"DebugLevel\":\"Error\"}}");
            var ignored = SettingPlatformInfo.Current == SettingPlatform.IOS;

            Assert.That(json.ContainsKey(nameof(DebugOptions.NoteFolding)), Is.EqualTo(!ignored));
            Assert.That(system!.Debug.NoteFolding, Is.EqualTo(ignored));
            Assert.That(system.Debug.DebugLevel, Is.EqualTo(ignored ? LogLevel.Info : LogLevel.Error));
        }
    }
}
