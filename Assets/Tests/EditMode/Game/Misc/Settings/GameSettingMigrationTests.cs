using System;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Settings
{
    /// <summary>
    /// Covers <see cref="GameSettingMigration"/>, which upgrades settings.json documents of older versions while the file
    /// is read, and its use by <see cref="MajEnv.DeserializeUserSettings"/>.
    /// </summary>
    public class GameSettingMigrationTests
    {
        /// <summary>Creates the serializer the settings.json reader passes to the migration.</summary>
        /// <returns>The serializer.</returns>
        private static JsonSerializer CreateSerializer()
        {
            return JsonSerializer.CreateDefault(MajEnv.UserJsonReaderOption);
        }

        /// <summary>Deserializes settings the way settings.json is read.</summary>
        /// <param name="json">The JSON text.</param>
        /// <returns>The settings.</returns>
        private static GameSetting Read(string json)
        {
            var setting = MajEnv.DeserializeUserSettings(json);
            Assert.That(setting, Is.Not.Null);
            return setting!;
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

        /// <summary>Checks that a document written by the current version is left unchanged.</summary>
        [Test]
        public void Migrate_CurrentDocumentIsUnchanged()
        {
            var setting = new GameSetting();
            setting.Rendering.RenderQuality = RenderingQualityOption.Refined;
            var document = JObject.Parse(Serializer.Json.Serialize(setting, MajEnv.UserJsonReaderOption));
            var original = (JObject)document.DeepClone();

            var changed = GameSettingMigration.Migrate(document, CreateSerializer());

            Assert.That(changed, Is.False);
            Assert.That(JToken.DeepEquals(document, original), Is.True);
        }

        /// <summary>Checks that the legacy rendering keys leave Display and form a Rendering category, keeping the other Display keys.</summary>
        [Test]
        public void Migrate_MovesLegacyRenderingKeysToRendering()
        {
            var document = JObject.Parse("{\"Display\":{\"Skin\":\"custom\",\"RenderQuality\":\"High\",\"RenderScale\":100,\"FPSLimit\":60,\"VSync\":false}}");

            var changed = GameSettingMigration.Migrate(document, CreateSerializer());

            var display = (JObject)document["Display"]!;
            var rendering = (JObject)document["Rendering"]!;
            Assert.That(changed, Is.True);
            Assert.That(display.Count, Is.EqualTo(1));
            Assert.That(display["Skin"]!.Value<string>(), Is.EqualTo("custom"));
            Assert.That(rendering["RenderQuality"]!.Value<string>(), Is.EqualTo("Beautiful"));
            Assert.That(rendering["FPSLimit"]!.Value<int>(), Is.EqualTo(60));
            Assert.That(rendering["VSync"]!.Value<bool>(), Is.False);
            Assert.That(GameSettingMigration.Migrate(document, CreateSerializer()), Is.False, "a migrated document is current");
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
            var rendering = Read(LegacyDocument(quality, scale)).Rendering;

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
            var rendering = Read(LegacyDocument(-1, 500)).Rendering;

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
            var rendering = Read("{\"Display\":{\"RenderQuality\":0}}").Rendering;

            Assert.That(rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.EnergySaving));
            Assert.That(rendering.TextureQuality, Is.EqualTo(RenderingTextureQualityOption.Medium));
        }

        /// <summary>Checks that legacy keys with null values are read as missing and still removed.</summary>
        [Test]
        public void Read_LegacyNullValuesAreMissing()
        {
            var document = JObject.Parse("{\"Display\":{\"RenderQuality\":null,\"RenderScale\":null,\"FPSLimit\":null,\"VSync\":null}}");

            var changed = GameSettingMigration.Migrate(document, CreateSerializer());
            var rendering = document.ToObject<GameSetting>(CreateSerializer())!.Rendering;

            Assert.That(changed, Is.True);
            Assert.That(((JObject)document["Display"]!).Count, Is.EqualTo(0));
            Assert.That(rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Performance));
            Assert.That(rendering.FPSLimit, Is.EqualTo(120));
            Assert.That(rendering.VSync, Is.True);
        }

        /// <summary>Checks that an existing Rendering category wins over legacy keys, including the frame pacing it stores.</summary>
        [Test]
        public void Read_ExistingRenderingWinsOverLegacy()
        {
            var rendering = Read("{\"Display\":{\"RenderQuality\":\"High\",\"FPSLimit\":60,\"VSync\":false},\"Rendering\":{\"RenderQuality\":\"Refined\",\"FPSLimit\":90,\"VSync\":true}}").Rendering;

            Assert.That(rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Refined));
            Assert.That(rendering.RenderScale, Is.EqualTo(150));
            Assert.That(rendering.FPSLimit, Is.EqualTo(90));
            Assert.That(rendering.VSync, Is.True);
        }

        /// <summary>Checks that legacy frame pacing fills in the keys missing from a newer Rendering category.</summary>
        [Test]
        public void Read_LegacyFramePacingFillsMissingKeys()
        {
            var rendering = Read("{\"Display\":{\"FPSLimit\":60,\"VSync\":false},\"Rendering\":{\"RenderQuality\":\"EnergySaving\"}}").Rendering;

            Assert.That(rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.EnergySaving));
            Assert.That(rendering.TextureQuality, Is.EqualTo(RenderingTextureQualityOption.Medium));
            Assert.That(rendering.FPSLimit, Is.EqualTo(60));
            Assert.That(rendering.VSync, Is.False);
        }

        /// <summary>Checks that a null Rendering category is read as missing, so the legacy keys still migrate.</summary>
        [Test]
        public void Read_NullRenderingMigratesLegacyKeys()
        {
            var rendering = Read("{\"Display\":{\"RenderQuality\":\"VeryHigh\",\"FPSLimit\":30},\"Rendering\":null}").Rendering;

            Assert.That(rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Custom));
            Assert.That(rendering.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.MSAA8x));
            Assert.That(rendering.FPSLimit, Is.EqualTo(30));
        }

        /// <summary>Checks that migrated settings are saved in the current layout and read back unchanged.</summary>
        [Test]
        public void Read_MigratedSettingsSaveCurrentLayout()
        {
            var migrated = Read(LegacyDocument(5, 150));

            var json = Serializer.Json.Serialize(migrated, MajEnv.UserJsonReaderOption);
            var saved = JObject.Parse(json);
            var restored = Read(json).Rendering;

            Assert.That(((JObject)saved["Display"]!).ContainsKey("RenderQuality"), Is.False);
            Assert.That(((JObject)saved["Display"]!).ContainsKey("RenderScale"), Is.False);
            Assert.That(saved["Rendering"]!["RenderQuality"]!.Value<string>(), Is.EqualTo("Custom"));
            Assert.That(restored.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.MSAA8x));
            Assert.That(restored.RenderScale, Is.EqualTo(150));
            Assert.That(restored.OutputMode, Is.EqualTo(RenderingOutputMode.Intermediate));
        }

        /// <summary>Checks that null categories are removed and reported as a change.</summary>
        [Test]
        public void Migrate_RemovesNullCategories()
        {
            var document = JObject.Parse("{\"Audio\":null,\"Game\":{\"TapSpeed\":8}}");

            var changed = GameSettingMigration.Migrate(document, CreateSerializer());

            Assert.That(changed, Is.True);
            Assert.That(document.ContainsKey("Audio"), Is.False);
            Assert.That(document["Game"]!["TapSpeed"]!.Value<float>(), Is.EqualTo(8f));
        }

        /// <summary>Checks that a Rendering category that is not an object is left for the deserializer to reject.</summary>
        [Test]
        public void Migrate_KeepsInvalidRenderingForTheDeserializer()
        {
            var document = JObject.Parse("{\"Display\":{\"FPSLimit\":60},\"Rendering\":5}");

            var changed = GameSettingMigration.Migrate(document, CreateSerializer());

            Assert.That(changed, Is.True);
            Assert.That(document["Rendering"]!.Value<int>(), Is.EqualTo(5));
            Assert.That(() => document.ToObject<GameSetting>(CreateSerializer()), Throws.InstanceOf<JsonException>());
        }

        /// <summary>Checks that a legacy quality level that cannot be read rejects the document, like any other invalid value.</summary>
        [Test]
        public void Migrate_InvalidLegacyQualityThrows()
        {
            var document = JObject.Parse("{\"Display\":{\"RenderQuality\":\"Bogus\"}}");

            Assert.That(() => GameSettingMigration.Migrate(document, CreateSerializer()), Throws.InstanceOf<JsonException>());
        }

        /// <summary>Checks the argument validation.</summary>
        [Test]
        public void Migrate_RejectsNullArguments()
        {
            Assert.That(() => GameSettingMigration.Migrate(null!, CreateSerializer()), Throws.InstanceOf<ArgumentNullException>());
            Assert.That(() => GameSettingMigration.Migrate(new JObject(), null!), Throws.InstanceOf<ArgumentNullException>());
        }
    }
}
