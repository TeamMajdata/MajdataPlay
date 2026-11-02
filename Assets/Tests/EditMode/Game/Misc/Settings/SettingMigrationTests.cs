using System;
using System.Collections.Generic;
using System.Reflection;
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
    /// Covers <see cref="SettingMigration"/>, which upgrades the settings.json and system-settings.json documents of
    /// older versions while the files are read, and its use by <see cref="MajEnv.DeserializeSettings"/>.
    /// </summary>
    public class SettingMigrationTests
    {
        /// <summary>The moved options that only exist on desktop platforms.</summary>
        private static readonly HashSet<string> s_standaloneOnlyOptions = new() { "Resolution", "Topmost", "FullScreen" };

        /// <summary>Creates the serializer the settings readers pass to the migration.</summary>
        /// <returns>The serializer.</returns>
        private static JsonSerializer CreateSerializer()
        {
            return JsonSerializer.CreateDefault(MajEnv.UserJsonReaderOption);
        }

        /// <summary>Deserializes settings the way the settings files are read.</summary>
        /// <param name="userJson">The text of settings.json.</param>
        /// <param name="systemJson">The text of system-settings.json, or null when the file does not exist.</param>
        /// <returns>The user and system settings.</returns>
        private static (UserSetting User, SystemSetting System) Read(string userJson, string? systemJson = null)
        {
            var (user, system) = MajEnv.DeserializeSettings(userJson, systemJson);
            Assert.That(user, Is.Not.Null);
            Assert.That(system, Is.Not.Null);
            return (user!, system!);
        }

        /// <summary>Creates a legacy document whose Display category holds the given rendering quality level and render scale.</summary>
        /// <param name="quality">The legacy quality level, or -1 to leave it out.</param>
        /// <param name="scale">The legacy render scale, or -1 to leave it out.</param>
        /// <returns>The JSON text.</returns>
        private static string LegacyDocument(int quality, int scale)
        {
            var display = new JObject();
            if (quality >= 0)
            {
                display["RenderQuality"] = ((RenderQualityOption)quality).ToString();
            }
            if (scale >= 0)
            {
                display["RenderScale"] = scale;
            }
            return new JObject { ["Display"] = display }.ToString();
        }

        /// <summary>Gets the type of a category of the current layout.</summary>
        /// <param name="category">The category name.</param>
        /// <returns>The type of the category property on <see cref="UserSetting"/> or <see cref="SystemSetting"/>.</returns>
        private static Type GetCategoryType(string category)
        {
            var property = typeof(UserSetting).GetProperty(category) ?? typeof(SystemSetting).GetProperty(category);
            Assert.That(property, Is.Not.Null, category);
            return property!.PropertyType;
        }

        /// <summary>Checks that documents written by the current version are left unchanged.</summary>
        [Test]
        public void Migrate_CurrentDocumentsAreUnchanged()
        {
            var system = new SystemSetting();
            system.Rendering.RenderQuality = RenderingQualityOption.Refined;
            var userDocument = JObject.Parse(Serializer.Json.Serialize(new UserSetting(), MajEnv.UserJsonReaderOption));
            var systemDocument = JObject.Parse(Serializer.Json.Serialize(system, MajEnv.UserJsonReaderOption));
            var originalUser = (JObject)userDocument.DeepClone();
            var originalSystem = (JObject)systemDocument.DeepClone();

            var changed = SettingMigration.Migrate(userDocument, systemDocument, CreateSerializer());

            Assert.That(changed, Is.False);
            Assert.That(JToken.DeepEquals(userDocument, originalUser), Is.True);
            Assert.That(JToken.DeepEquals(systemDocument, originalSystem), Is.True);
        }

        /// <summary>Checks that the system categories of a legacy settings.json move to system-settings.json and the user categories stay.</summary>
        [Test]
        public void Migrate_SplitsLegacySettingsIntoUserAndSystemDocuments()
        {
            var user = JObject.Parse("{\"Game\":{\"TapSpeed\":8},\"Judge\":{},\"Display\":{},\"Rendering\":{\"FPSLimit\":60}," +
                                     "\"Audio\":{\"ForceMono\":true},\"Encoding\":{},\"Decoding\":{},\"Debug\":{\"DebugLevel\":\"Warning\"}," +
                                     "\"Online\":{\"Enable\":true},\"IO\":{}}");
            var system = new JObject();

            var changed = SettingMigration.Migrate(user, system, CreateSerializer());

            Assert.That(changed, Is.True);
            Assert.That(user.Properties(), Has.All.Matches<JProperty>(category => category.Name is "Game" or "Judge" or "Display"));
            Assert.That(user["Game"]!["TapSpeed"]!.Value<float>(), Is.EqualTo(8f));
            Assert.That(system["Rendering"]!["FPSLimit"]!.Value<int>(), Is.EqualTo(60));
            Assert.That(system["Audio"]!["ForceMono"]!.Value<bool>(), Is.True);
            Assert.That(system["Debug"]!["DebugLevel"]!.Value<string>(), Is.EqualTo("Warning"));
            Assert.That(system.ContainsKey("Online"), Is.False);
            Assert.That(system["Network"]!["Online"]!["Enable"]!.Value<bool>(), Is.True);
            Assert.That(system.ContainsKey("Encoding") && system.ContainsKey("Decoding") && system.ContainsKey("IO"), Is.True);
            Assert.That(SettingMigration.Migrate(user, system, CreateSerializer()), Is.False, "migrated documents are current");
        }

        /// <summary>Checks that a category system-settings.json already has keeps its values and only receives the keys it lacks.</summary>
        [Test]
        public void Migrate_ExistingSystemCategoryWinsOverLegacy()
        {
            var user = JObject.Parse("{\"Debug\":{\"DebugLevel\":\"Warning\",\"MaxQueuedFrames\":3},\"Audio\":5}");
            var system = JObject.Parse("{\"Debug\":{\"DebugLevel\":\"Error\"},\"Audio\":{\"ForceMono\":true}}");

            var changed = SettingMigration.Migrate(user, system, CreateSerializer());

            Assert.That(changed, Is.True);
            Assert.That(user.Count, Is.EqualTo(0));
            Assert.That(system["Debug"]!["DebugLevel"]!.Value<string>(), Is.EqualTo("Error"));
            Assert.That(system["Debug"]!["MaxQueuedFrames"]!.Value<int>(), Is.EqualTo(3));
            Assert.That(system["Audio"]!["ForceMono"]!.Value<bool>(), Is.True);
        }

        /// <summary>
        /// Checks that every moved option still is a property of its current category and no longer one of its legacy
        /// category, so the migration table follows renames and its keys stay unambiguous.
        /// </summary>
        [Test]
        public void LegacyOptionMoves_NameCurrentProperties()
        {
            foreach (var move in SettingMigration.LegacyOptionMoves)
            {
                var source = GetCategoryType(move.SourceCategory);
                var target = GetCategoryType(move.TargetCategory);
                foreach (var (legacyKey, currentKey) in move.Keys)
                {
                    Assert.That(source.GetProperty(legacyKey, BindingFlags.Public | BindingFlags.Instance), Is.Null, $"{move.SourceCategory}.{legacyKey}");
#if !UNITY_STANDALONE
                    if (s_standaloneOnlyOptions.Contains(currentKey))
                    {
                        continue;
                    }
#endif
                    Assert.That(target.GetProperty(currentKey, BindingFlags.Public | BindingFlags.Instance), Is.Not.Null, $"{move.TargetCategory}.{currentKey}");
                }
            }
        }

        /// <summary>Checks that every moved option leaves its legacy category and arrives in its current category with its value.</summary>
        [Test]
        public void Migrate_MovesEveryLegacyOptionToItsCurrentCategory()
        {
            var user = new JObject();
            var system = new JObject();
            var index = 0;
            foreach (var move in SettingMigration.LegacyOptionMoves)
            {
                var root = move.SourceCategory is "Game" or "Display" ? user : system;
                if (root[move.SourceCategory] is not JObject source)
                {
                    // Assigning a token that already has a parent stores a copy, so only a new category is assigned.
                    source = new JObject();
                    root[move.SourceCategory] = source;
                }
                foreach (var (legacyKey, _) in move.Keys)
                {
                    source[legacyKey] = $"value{index++}";
                }
            }

            SettingMigration.Migrate(user, system, CreateSerializer());

            index = 0;
            foreach (var move in SettingMigration.LegacyOptionMoves)
            {
                var targetRoot = typeof(UserSetting).GetProperty(move.TargetCategory) is not null ? user : system;
                foreach (var (legacyKey, currentKey) in move.Keys)
                {
                    Assert.That(targetRoot[move.TargetCategory]![currentKey]!.Value<string>(), Is.EqualTo($"value{index++}"), $"{move.TargetCategory}.{currentKey}");
                    var sourceRoot = move.SourceCategory is "Game" or "Display" ? user : system;
                    Assert.That(((JObject)sourceRoot[move.SourceCategory]!).ContainsKey(legacyKey), Is.False, $"{move.SourceCategory}.{legacyKey}");
                }
            }
        }

        /// <summary>Checks the values of moved options after reading a legacy settings.json.</summary>
        [Test]
        public void Read_LegacySettingsKeepTheirMovedValues()
        {
            var (user, system) = Read(
                "{\"Game\":{\"TapSpeed\":8.5,\"BGInfo\":\"None\",\"TopInfo\":\"Judge\",\"EnableJudgeTimingGauge\":true,\"RecordMode\":\"FFmpeg\"}," +
                "\"Display\":{\"Skin\":\"custom\",\"SkipVideoDownload\":true,\"FastLateType\":\"All\",\"InnerJudgeDistance\":0.5," +
                "\"MainScreenTransform\":true,\"MainScreenScale\":0.8,\"GameplayScreenRotationAngle\":\"_180\"}," +
                "\"Audio\":{\"ForceMono\":true,\"Volume\":{\"Global\":0.9,\"Tap\":0.1}}," +
                "\"Debug\":{\"DebugLevel\":\"Warning\",\"NoteAppearRate\":0.3,\"OffsetUnit\":\"Second\",\"DisplaySensor\":true," +
                "\"SensorOpacity\":40,\"TouchRadiusAdjust\":0.3,\"TouchCAreaExtraRadius\":0.5,\"DisplayRuntimeInfo\":false}," +
                "\"Online\":{\"Enable\":true,\"ApiEndpoints\":[]}}");

            Assert.That(user.Game.TapSpeed, Is.EqualTo(8.5f));
            Assert.That(user.Game.SkipVideoDownload, Is.True);
            Assert.That(user.Game.NoteAppearRate, Is.EqualTo(0.3f));
            Assert.That(user.Judge.EnableJudgeTimingGauge, Is.True);
            Assert.That(user.Judge.OffsetUnit, Is.EqualTo(OffsetUnitOption.Second));
            Assert.That(user.Judge.FastLateType, Is.EqualTo(JudgeDisplayOption.All));
            Assert.That(user.Judge.InnerJudgeDistance, Is.EqualTo(0.5f));
            Assert.That(user.Display.Skin, Is.EqualTo("custom"));
            Assert.That(user.Display.BGInfo, Is.EqualTo(BGInfoOption.None));
            Assert.That(user.Display.TopInfo, Is.EqualTo(TopInfoDisplayOption.Judge));
            Assert.That(user.Display.DisplaySensor, Is.True);
            Assert.That(user.Display.SensorOpacity, Is.EqualTo(40));
            Assert.That(user.Volume.Global, Is.EqualTo(0.9f));
            Assert.That(user.Volume.Tap, Is.EqualTo(0.1f));
            Assert.That(user.Touch.TouchRadiusAdjust, Is.EqualTo(0.3f));
            Assert.That(user.Touch.TouchCAreaExtraRadius, Is.EqualTo(0.5f));
            Assert.That(system.Common.RecordMode, Is.EqualTo(RecordModeOption.FFmpeg));
            Assert.That(system.Audio.ForceMono, Is.True);
            Assert.That(system.Debug.DebugLevel, Is.EqualTo(LogLevel.Warning));
            Assert.That(system.Rendering.MainScreenTransform, Is.True);
            Assert.That(system.Rendering.MainScreenScale, Is.EqualTo(0.8f));
            Assert.That(system.Rendering.GameplayScreenRotationAngle, Is.EqualTo(GameplayScreenRotationAngleOption._180));
            Assert.That(system.Rendering.DisplayRuntimeInfo, Is.False);
            Assert.That(system.Network.Online.ApiEndpoints, Is.Empty);
#if !UNITY_IOS
            Assert.That(system.Network.Online.Enable, Is.True);
#endif
        }

        /// <summary>
        /// Checks that the general options and the renamed Vulkan frame pacing options of a legacy <c>Debug</c> category
        /// keep their values in <c>Common</c> and <c>Rendering</c>.
        /// </summary>
        [Test]
        public void Read_LegacyDebugOptionsMoveToCommonAndRendering()
        {
            var (_, system) = Read("{}",
                "{\"Debug\":{\"DebugLevel\":\"Warning\",\"MenuOptionIterationSpeed\":30,\"HideCursorInGame\":false," +
                "\"NoteFolding\":false,\"DJAutoPolicy\":\"Permissive\",\"LowLatencyVSync\":false," +
                "\"LowLatencyVSyncBackend\":\"Timer\"}}");

            Assert.That(system.Debug.DebugLevel, Is.EqualTo(LogLevel.Warning));
            Assert.That(system.Common.MenuOptionIterationSpeed, Is.EqualTo(30));
            Assert.That(system.Common.HideCursorInGame, Is.False);
            Assert.That(system.Common.DJAutoPolicy, Is.EqualTo(DJAutoPolicyOption.Permissive));
            Assert.That(system.Rendering.VulkanLowLatencyVSync, Is.False);
            Assert.That(system.Rendering.VulkanLowLatencyVSyncBackend, Is.EqualTo(VulkanLowLatencyVSyncBackendOption.Timer));
#if !UNITY_IOS
            Assert.That(system.Common.NoteFolding, Is.False);
#endif
        }

        /// <summary>Checks that options already stored in their current category win over the same options in their legacy category.</summary>
        [Test]
        public void Read_CurrentOptionsWinOverLegacyOptions()
        {
            var (user, system) = Read(
                "{\"Game\":{\"BGInfo\":\"None\",\"RecordMode\":\"OBSTrigger\"},\"Display\":{\"BGInfo\":\"Achievement\",\"MainScreenScale\":0.8}," +
                "\"Volume\":{\"Global\":0.4},\"Audio\":{\"Volume\":{\"Global\":0.9}},\"Debug\":{\"OffsetUnit\":\"Second\"}}",
                "{\"Common\":{\"RecordMode\":\"FFmpeg\"},\"Rendering\":{\"MainScreenScale\":0.6}}");

            Assert.That(user.Display.BGInfo, Is.EqualTo(BGInfoOption.Achievement));
            Assert.That(user.Volume.Global, Is.EqualTo(0.4f));
            Assert.That(user.Judge.OffsetUnit, Is.EqualTo(OffsetUnitOption.Second));
            Assert.That(system.Common.RecordMode, Is.EqualTo(RecordModeOption.FFmpeg));
            Assert.That(system.Rendering.MainScreenScale, Is.EqualTo(0.6f));
        }

        /// <summary>Checks that the legacy rendering keys leave Display and form a Rendering category of system-settings.json, keeping the other Display keys.</summary>
        [Test]
        public void Migrate_MovesLegacyRenderingKeysToRendering()
        {
            var user = JObject.Parse("{\"Display\":{\"Skin\":\"custom\",\"RenderQuality\":\"High\",\"RenderScale\":100,\"FPSLimit\":60,\"VSync\":false}}");
            var system = new JObject();

            var changed = SettingMigration.Migrate(user, system, CreateSerializer());

            var display = (JObject)user["Display"]!;
            var rendering = (JObject)system["Rendering"]!;
            Assert.That(changed, Is.True);
            Assert.That(user.ContainsKey("Rendering"), Is.False);
            Assert.That(display.Count, Is.EqualTo(1));
            Assert.That(display["Skin"]!.Value<string>(), Is.EqualTo("custom"));
            Assert.That(rendering["RenderQuality"]!.Value<string>(), Is.EqualTo("Beautiful"));
            Assert.That(rendering["FPSLimit"]!.Value<int>(), Is.EqualTo(60));
            Assert.That(rendering["VSync"]!.Value<bool>(), Is.False);
            Assert.That(SettingMigration.Migrate(user, system, CreateSerializer()), Is.False, "migrated documents are current");
        }

        /// <summary>Checks the mapping of the six legacy quality levels and render scales that do not depend on the platform.</summary>
        /// <param name="quality">The legacy quality, or -1 for none.</param>
        /// <param name="scale">The legacy render scale, or -1 for none.</param>
        /// <param name="expectedQuality">The expected preset.</param>
        /// <param name="expectedAntiAliasing">The expected anti-aliasing.</param>
        /// <param name="expectedTexture">The expected texture quality.</param>
        /// <param name="expectedScale">The expected render scale.</param>
        /// <param name="expectedOutput">The expected output mode.</param>
        [TestCase(-1, -1, RenderingQualityOption.Performance, RenderingAntialiasingOption.Disabled, RenderingTextureQualityOption.High, 100, RenderingOutputMode.Direct)]
        [TestCase(0, 100, RenderingQualityOption.EnergySaving, RenderingAntialiasingOption.Disabled, RenderingTextureQualityOption.Medium, 100, RenderingOutputMode.Direct)]
        [TestCase(0, 80, RenderingQualityOption.Custom, RenderingAntialiasingOption.Disabled, RenderingTextureQualityOption.Medium, 80, RenderingOutputMode.Intermediate)]
        [TestCase(1, 100, RenderingQualityOption.Performance, RenderingAntialiasingOption.Disabled, RenderingTextureQualityOption.High, 100, RenderingOutputMode.Direct)]
        [TestCase(2, -1, RenderingQualityOption.Custom, RenderingAntialiasingOption.MSAA2x, RenderingTextureQualityOption.High, 100, RenderingOutputMode.Direct)]
        [TestCase(3, -1, RenderingQualityOption.Beautiful, RenderingAntialiasingOption.MSAA4x, RenderingTextureQualityOption.High, 100, RenderingOutputMode.Direct)]
        [TestCase(3, 100, RenderingQualityOption.Beautiful, RenderingAntialiasingOption.MSAA4x, RenderingTextureQualityOption.High, 100, RenderingOutputMode.Direct)]
        [TestCase(4, 100, RenderingQualityOption.Custom, RenderingAntialiasingOption.MSAA8x, RenderingTextureQualityOption.High, 100, RenderingOutputMode.Direct)]
        [TestCase(5, 150, RenderingQualityOption.Custom, RenderingAntialiasingOption.MSAA8x, RenderingTextureQualityOption.High, 150, RenderingOutputMode.Intermediate)]
        [TestCase(3, 50, RenderingQualityOption.Custom, RenderingAntialiasingOption.MSAA4x, RenderingTextureQualityOption.High, 50, RenderingOutputMode.Intermediate)]
        [TestCase(3, 5, RenderingQualityOption.Custom, RenderingAntialiasingOption.MSAA4x, RenderingTextureQualityOption.High, 10, RenderingOutputMode.Intermediate)]
        public void Read_MapsLegacyQualityLevels(int quality, int scale, RenderingQualityOption expectedQuality, RenderingAntialiasingOption expectedAntiAliasing,
                                                 RenderingTextureQualityOption expectedTexture, int expectedScale, RenderingOutputMode expectedOutput)
        {
            var rendering = Read(LegacyDocument(quality, scale)).System.Rendering;

            Assert.That(rendering.RenderQuality, Is.EqualTo(expectedQuality));
            Assert.That(rendering.AntiAliasing, Is.EqualTo(expectedAntiAliasing));
            Assert.That(rendering.TextureQuality, Is.EqualTo(expectedTexture));
            Assert.That(rendering.RenderScale, Is.EqualTo(expectedScale));
            Assert.That(rendering.OutputMode, Is.EqualTo(expectedOutput));
        }

        /// <summary>Checks that a legacy render scale without a quality level completes it with the old platform default.</summary>
        [Test]
        public void Read_LegacyScaleUsesPlatformDefaultQuality()
        {
            var rendering = Read(LegacyDocument(-1, 500)).System.Rendering;

            Assert.That(rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Custom));
            Assert.That(rendering.RenderScale, Is.EqualTo(200));
            Assert.That(rendering.OutputMode, Is.EqualTo(RenderingOutputMode.Intermediate));
#if UNITY_ANDROID || UNITY_IOS
            Assert.That(rendering.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.Disabled));
#else
            Assert.That(rendering.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.MSAA2x));
#endif
        }

        /// <summary>Checks that a legacy quality level stored as a number is read like its name.</summary>
        [Test]
        public void Read_LegacyNumericQuality()
        {
            var rendering = Read("{\"Display\":{\"RenderQuality\":0}}").System.Rendering;

            Assert.That(rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.EnergySaving));
            Assert.That(rendering.TextureQuality, Is.EqualTo(RenderingTextureQualityOption.Medium));
        }

        /// <summary>Checks that legacy keys with null values are read as missing and still removed.</summary>
        [Test]
        public void Read_LegacyNullValuesAreMissing()
        {
            var user = JObject.Parse("{\"Display\":{\"RenderQuality\":null,\"RenderScale\":null,\"FPSLimit\":null,\"VSync\":null,\"MainScreenScale\":null}}");
            var system = new JObject();

            var changed = SettingMigration.Migrate(user, system, CreateSerializer());
            var rendering = system.ToObject<SystemSetting>(CreateSerializer())!.Rendering;

            Assert.That(changed, Is.True);
            Assert.That(((JObject)user["Display"]!).Count, Is.EqualTo(0));
            Assert.That(rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Performance));
            Assert.That(rendering.FPSLimit, Is.EqualTo(120));
            Assert.That(rendering.VSync, Is.True);
            Assert.That(rendering.MainScreenScale, Is.EqualTo(1f));
        }

        /// <summary>Checks that an existing Rendering category wins over legacy keys, including the frame pacing it stores.</summary>
        [Test]
        public void Read_ExistingRenderingWinsOverLegacy()
        {
            var rendering = Read("{\"Display\":{\"RenderQuality\":\"High\",\"FPSLimit\":60,\"VSync\":false},\"Rendering\":{\"RenderQuality\":\"Refined\",\"FPSLimit\":90,\"VSync\":true}}").System.Rendering;

            Assert.That(rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Refined));
            Assert.That(rendering.RenderScale, Is.EqualTo(150));
            Assert.That(rendering.FPSLimit, Is.EqualTo(90));
            Assert.That(rendering.VSync, Is.True);
        }

        /// <summary>Checks that the Rendering category of system-settings.json wins over the legacy keys of settings.json.</summary>
        [Test]
        public void Read_SystemRenderingWinsOverLegacy()
        {
            var rendering = Read("{\"Display\":{\"RenderQuality\":\"High\",\"FPSLimit\":60}}", "{\"Rendering\":{\"RenderQuality\":\"Refined\",\"FPSLimit\":90}}").System.Rendering;

            Assert.That(rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Refined));
            Assert.That(rendering.FPSLimit, Is.EqualTo(90));
        }

        /// <summary>Checks that legacy frame pacing fills in the keys missing from a newer Rendering category.</summary>
        [Test]
        public void Read_LegacyFramePacingFillsMissingKeys()
        {
            var rendering = Read("{\"Display\":{\"FPSLimit\":60,\"VSync\":false},\"Rendering\":{\"RenderQuality\":\"EnergySaving\"}}").System.Rendering;

            Assert.That(rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.EnergySaving));
            Assert.That(rendering.TextureQuality, Is.EqualTo(RenderingTextureQualityOption.Medium));
            Assert.That(rendering.FPSLimit, Is.EqualTo(60));
            Assert.That(rendering.VSync, Is.False);
        }

        /// <summary>Checks that a null Rendering category is read as missing, so the legacy keys still migrate.</summary>
        [Test]
        public void Read_NullRenderingMigratesLegacyKeys()
        {
            var rendering = Read("{\"Display\":{\"RenderQuality\":\"VeryHigh\",\"FPSLimit\":30},\"Rendering\":null}").System.Rendering;

            Assert.That(rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Custom));
            Assert.That(rendering.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.MSAA8x));
            Assert.That(rendering.FPSLimit, Is.EqualTo(30));
        }

        /// <summary>Checks that migrated settings are saved in the current layouts and read back unchanged.</summary>
        [Test]
        public void Read_MigratedSettingsSaveCurrentLayout()
        {
            var (migratedUser, migratedSystem) = Read(LegacyDocument(5, 150));

            var userJson = Serializer.Json.Serialize(migratedUser, MajEnv.UserJsonReaderOption);
            var systemJson = Serializer.Json.Serialize(migratedSystem, MajEnv.UserJsonReaderOption);
            var savedUser = JObject.Parse(userJson);
            var savedSystem = JObject.Parse(systemJson);
            var restored = Read(userJson, systemJson).System.Rendering;

            Assert.That(((JObject)savedUser["Display"]!).ContainsKey("RenderQuality"), Is.False);
            Assert.That(((JObject)savedUser["Display"]!).ContainsKey("RenderScale"), Is.False);
            Assert.That(savedUser.ContainsKey("Rendering"), Is.False);
            Assert.That(savedSystem["Rendering"]!["RenderQuality"]!.Value<string>(), Is.EqualTo("Custom"));
            Assert.That(restored.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.MSAA8x));
            Assert.That(restored.RenderScale, Is.EqualTo(150));
            Assert.That(restored.OutputMode, Is.EqualTo(RenderingOutputMode.Intermediate));
        }

        /// <summary>Checks that null categories are removed from both documents and reported as a change.</summary>
        [Test]
        public void Migrate_RemovesNullCategories()
        {
            var user = JObject.Parse("{\"Audio\":null,\"Game\":{\"TapSpeed\":8},\"Touch\":null}");
            var system = JObject.Parse("{\"Debug\":null}");

            var changed = SettingMigration.Migrate(user, system, CreateSerializer());

            Assert.That(changed, Is.True);
            Assert.That(user.ContainsKey("Audio"), Is.False);
            Assert.That(user.ContainsKey("Touch"), Is.False);
            Assert.That(system.ContainsKey("Audio"), Is.False);
            Assert.That(system.ContainsKey("Debug"), Is.False);
            Assert.That(user["Game"]!["TapSpeed"]!.Value<float>(), Is.EqualTo(8f));
        }

        /// <summary>Checks that a Rendering category that is not an object is left for the deserializer to reject.</summary>
        [Test]
        public void Migrate_KeepsInvalidRenderingForTheDeserializer()
        {
            var user = JObject.Parse("{\"Display\":{\"FPSLimit\":60,\"MainScreenScale\":0.5},\"Rendering\":5}");
            var system = new JObject();

            var changed = SettingMigration.Migrate(user, system, CreateSerializer());

            Assert.That(changed, Is.True);
            Assert.That(system["Rendering"]!.Value<int>(), Is.EqualTo(5));
            Assert.That(((JObject)user["Display"]!).Count, Is.EqualTo(0));
            Assert.That(() => system.ToObject<SystemSetting>(CreateSerializer()), Throws.InstanceOf<JsonException>());
        }

        /// <summary>
        /// Checks that the legacy Online category of system-settings.json moves into the Network category: the proxy keys
        /// form the Proxy options and the other keys keep their names in the Online options.
        /// </summary>
        [Test]
        public void Migrate_MovesLegacyOnlineCategoryToNetwork()
        {
            var user = new JObject();
            var system = JObject.Parse("{\"Online\":{\"Enable\":true,\"UseProxy\":false,\"Proxy\":\"http://proxy.invalid:1\"," +
                                       "\"ApiEndpoints\":[{\"Name\":\"n\",\"Url\":\"https://example.invalid/\"}]}}");

            var changed = SettingMigration.Migrate(user, system, CreateSerializer());

            Assert.That(changed, Is.True);
            Assert.That(system.ContainsKey("Online"), Is.False);
            var network = (JObject)system["Network"]!;
            var online = (JObject)network["Online"]!;
            var proxy = (JObject)network["Proxy"]!;
            Assert.That(network.Count, Is.EqualTo(2));
            Assert.That(online.Count, Is.EqualTo(2));
            Assert.That(online["Enable"]!.Value<bool>(), Is.True);
            Assert.That(online["ApiEndpoints"]![0]!["Name"]!.Value<string>(), Is.EqualTo("n"));
            Assert.That(proxy.Count, Is.EqualTo(2));
            Assert.That(proxy["Enable"]!.Value<bool>(), Is.False);
            Assert.That(proxy["Url"]!.Value<string>(), Is.EqualTo("http://proxy.invalid:1"));
            Assert.That(SettingMigration.Migrate(user, system, CreateSerializer()), Is.False, "migrated documents are current");
        }

        /// <summary>Checks that the Network options system-settings.json already has keep their values and only receive the legacy keys they lack.</summary>
        [Test]
        public void Read_ExistingNetworkWinsOverLegacyOnline()
        {
            var (_, system) = Read("{}",
                "{\"Online\":{\"Enable\":true,\"ApiEndpoints\":[{\"Name\":\"legacy\",\"Url\":\"https://legacy.invalid/\"}]}," +
                "\"Network\":{\"Online\":{\"Enable\":false}}}");

            Assert.That(system.Network.Online.Enable, Is.False);
            Assert.That(system.Network.Online.ApiEndpoints, Has.Length.EqualTo(1));
            Assert.That(system.Network.Online.ApiEndpoints[0].Name, Is.EqualTo("legacy"));
        }

        /// <summary>Checks that the legacy proxy keys fill the proxy options and lose to the proxy options system-settings.json already has.</summary>
        [Test]
        public void Read_LegacyProxyKeysFillTheProxyOptions()
        {
            var (_, system) = Read("{}",
                "{\"Online\":{\"UseProxy\":false,\"Proxy\":\"http://legacy.invalid:1\"}," +
                "\"Network\":{\"Proxy\":{\"Url\":\"http://current.invalid:2\"}}}");

            Assert.That(system.Network.Proxy.Enable, Is.False);
            Assert.That(system.Network.Proxy.Url, Is.EqualTo("http://current.invalid:2"));
        }

        /// <summary>
        /// Checks that Network options that are not objects are left for the deserializer to reject, and the legacy Online
        /// values meant for them are dropped.
        /// </summary>
        [Test]
        public void Migrate_KeepsInvalidNetworkForTheDeserializer()
        {
            var system = JObject.Parse("{\"Online\":{\"Enable\":true},\"Network\":5}");
            var invalidProxy = JObject.Parse("{\"Online\":{\"Enable\":true,\"UseProxy\":false},\"Network\":{\"Proxy\":5}}");

            var changed = SettingMigration.Migrate(new JObject(), system, CreateSerializer());
            var proxyChanged = SettingMigration.Migrate(new JObject(), invalidProxy, CreateSerializer());

            Assert.That(changed, Is.True);
            Assert.That(system.ContainsKey("Online"), Is.False);
            Assert.That(system["Network"]!.Value<int>(), Is.EqualTo(5));
            Assert.That(() => system.ToObject<SystemSetting>(CreateSerializer()), Throws.InstanceOf<JsonException>());
            Assert.That(proxyChanged, Is.True);
            Assert.That(invalidProxy["Network"]!["Proxy"]!.Value<int>(), Is.EqualTo(5));
            Assert.That(invalidProxy["Network"]!["Online"]!["Enable"]!.Value<bool>(), Is.True);
        }

        /// <summary>
        /// Checks that the legacy player number leaves the InputDevice options for the IO category, keeping the other
        /// InputDevice keys.
        /// </summary>
        [Test]
        public void Migrate_MovesLegacyPlayerToIO()
        {
            var system = JObject.Parse("{\"IO\":{\"InputDevice\":{\"Player\":2,\"ButtonRing\":{\"Enable\":false}}}}");

            var changed = SettingMigration.Migrate(new JObject(), system, CreateSerializer());

            Assert.That(changed, Is.True);
            var io = (JObject)system["IO"]!;
            var inputDevice = (JObject)io["InputDevice"]!;
            Assert.That(io[SettingMigration.PlayerKey]!.Value<int>(), Is.EqualTo(2));
            Assert.That(inputDevice.ContainsKey(SettingMigration.PlayerKey), Is.False);
            Assert.That(inputDevice["ButtonRing"]!["Enable"]!.Value<bool>(), Is.False);
            Assert.That(SettingMigration.Migrate(new JObject(), system, CreateSerializer()), Is.False, "migrated documents are current");
        }

        /// <summary>Checks that a player number the IO category already has wins over the legacy one, which is still removed.</summary>
        [Test]
        public void Migrate_ExistingPlayerWinsOverLegacy()
        {
            var system = JObject.Parse("{\"IO\":{\"Player\":1,\"InputDevice\":{\"Player\":2}}}");

            var changed = SettingMigration.Migrate(new JObject(), system, CreateSerializer());

            Assert.That(changed, Is.True);
            Assert.That(system["IO"]![SettingMigration.PlayerKey]!.Value<int>(), Is.EqualTo(1));
            Assert.That(((JObject)system["IO"]!["InputDevice"]!).ContainsKey(SettingMigration.PlayerKey), Is.False);
        }

        /// <summary>Checks that IO categories without InputDevice options, or with ones that are not objects, are left unchanged.</summary>
        [Test]
        public void Migrate_IgnoresIOWithoutInputDeviceOptions()
        {
            var withoutInputDevice = JObject.Parse("{\"IO\":{\"Player\":2}}");
            var invalidInputDevice = JObject.Parse("{\"IO\":{\"InputDevice\":5}}");
            var originalWithout = withoutInputDevice.DeepClone();
            var originalInvalid = invalidInputDevice.DeepClone();

            Assert.That(SettingMigration.Migrate(new JObject(), withoutInputDevice, CreateSerializer()), Is.False);
            Assert.That(SettingMigration.Migrate(new JObject(), invalidInputDevice, CreateSerializer()), Is.False);
            Assert.That(JToken.DeepEquals(withoutInputDevice, originalWithout), Is.True);
            Assert.That(JToken.DeepEquals(invalidInputDevice, originalInvalid), Is.True);
        }

#if UNITY_STANDALONE
        /// <summary>Checks that the migrated player number key is a property of the IO options and no longer one of the InputDevice options.</summary>
        [Test]
        public void PlayerKey_NamesTheIOProperty()
        {
            Assert.That(typeof(IOOptions).GetProperty(SettingMigration.PlayerKey), Is.Not.Null);
            Assert.That(typeof(InputDeviceOptions).GetProperty(SettingMigration.PlayerKey), Is.Null);
        }

        /// <summary>Checks the player number after reading a legacy settings.json and a legacy system-settings.json.</summary>
        [Test]
        public void Read_LegacyPlayerKeepsItsValue()
        {
            var (_, fromSettings) = Read("{\"IO\":{\"InputDevice\":{\"Player\":2}}}");
            var (_, fromSystemSettings) = Read("{}", "{\"IO\":{\"InputDevice\":{\"Player\":2}}}");

            Assert.That(fromSettings.IO.Player, Is.EqualTo(2));
            Assert.That(fromSystemSettings.IO.Player, Is.EqualTo(2));
        }
#endif

        /// <summary>Checks that a legacy quality level that cannot be read rejects the document, like any other invalid value.</summary>
        [Test]
        public void Migrate_InvalidLegacyQualityThrows()
        {
            var user = JObject.Parse("{\"Display\":{\"RenderQuality\":\"Bogus\"}}");

            Assert.That(() => SettingMigration.Migrate(user, new JObject(), CreateSerializer()), Throws.InstanceOf<JsonException>());
        }

        /// <summary>Checks the argument validation.</summary>
        [Test]
        public void Migrate_RejectsNullArguments()
        {
            Assert.That(() => SettingMigration.Migrate(null!, new JObject(), CreateSerializer()), Throws.InstanceOf<ArgumentNullException>());
            Assert.That(() => SettingMigration.Migrate(new JObject(), null!, CreateSerializer()), Throws.InstanceOf<ArgumentNullException>());
            Assert.That(() => SettingMigration.Migrate(new JObject(), new JObject(), null!), Throws.InstanceOf<ArgumentNullException>());
        }
    }
}
