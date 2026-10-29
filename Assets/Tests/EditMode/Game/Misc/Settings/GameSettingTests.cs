using MajdataPlay.Diagnostics;
using MajdataPlay.i18n;
using MajdataPlay.Settings;
using MajdataPlay.Settings.Runtime;
using MajdataPlay.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Settings
{
    /// <summary>Covers the defaults, JSON round trip and settings.json reading of <see cref="GameSetting"/>.</summary>
    /// <remarks>The upgrade of legacy keys is covered by <see cref="GameSettingMigrationTests"/>.</remarks>
    public class GameSettingTests
    {
        /// <summary>Serializes settings with the reader options used for settings.json.</summary>
        /// <param name="setting">The settings to write.</param>
        /// <returns>The JSON text.</returns>
        private static string Write(GameSetting setting)
        {
            return Serializer.Json.Serialize(setting, MajEnv.UserJsonReaderOption);
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

        /// <summary>Checks the defaults that the game relies on during first startup.</summary>
        [Test]
        public void Defaults_MatchFirstStartup()
        {
            var setting = new GameSetting();

            Assert.That(setting.Game.TapSpeed, Is.EqualTo(7.5f));
            Assert.That(setting.Game.LeadInTime, Is.EqualTo(1));
            Assert.That(setting.Judge.Mode, Is.EqualTo(JudgeModeOption.Modern));
            Assert.That(setting.Display.Skin, Is.EqualTo("default"));
            Assert.That(setting.Display.Language, Is.Empty);
            Assert.That(setting.Display.SlidePathStyle, Is.EqualTo(SlidePathStyle.Classic));
            Assert.That(setting.Rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Performance));
            Assert.That(setting.Audio.Bass.BufferLengthMs, Is.EqualTo(1000));
            Assert.That(setting.Audio.Volume.Global, Is.EqualTo(0.3f));
            Assert.That(setting.Debug.OffsetUnit, Is.EqualTo(OffsetUnitOption.Frame));
            Assert.That(setting.Debug.DebugLevel, Is.EqualTo(LogLevel.Info));
            Assert.That(setting.Online.Enable, Is.False);
            Assert.That(setting.Mod.IsAnyModActive(), Is.False);
#if UNITY_ANDROID || UNITY_IOS
            Assert.That(setting.Game.GameplaySubScreenClickBehavior, Is.EqualTo(GameplaySubScreenClickBehaviorOption.TrackSkip_1_Sec_Delay));
            Assert.That(setting.Display.MainScreenTransform, Is.True);
            Assert.That(setting.Audio.Backend, Is.EqualTo(SoundBackendOption.BassSimple));
            Assert.That(setting.Audio.Bass.DeviceBufferLengthMs, Is.EqualTo(32));
            Assert.That(setting.Audio.Bass.DeviceUpdatePeriodMs, Is.EqualTo(8));
            Assert.That(setting.Debug.TapPoolCapacity, Is.EqualTo(48));
#else
            Assert.That(setting.Game.GameplaySubScreenClickBehavior, Is.EqualTo(GameplaySubScreenClickBehaviorOption.TrackSkip));
            Assert.That(setting.Display.MainScreenTransform, Is.False);
            Assert.That(setting.Audio.Backend, Is.EqualTo(SoundBackendOption.Wasapi));
            Assert.That(setting.Audio.Bass.DeviceBufferLengthMs, Is.EqualTo(64));
            Assert.That(setting.Audio.Bass.DeviceUpdatePeriodMs, Is.EqualTo(16));
            Assert.That(setting.Debug.TapPoolCapacity, Is.EqualTo(96));
#endif
#if UNITY_IOS
            Assert.That(setting.Online.ApiEndpoints, Is.Empty);
#else
            Assert.That(setting.Online.ApiEndpoints, Has.Length.EqualTo(1));
            Assert.That(setting.Online.ApiEndpoints[0].Name, Is.EqualTo("MajdataNET"));
#endif
#if UNITY_STANDALONE
            Assert.That(setting.Display.Resolution, Is.EqualTo("1080x1920"));
            Assert.That(setting.Audio.Channel.FrontVolume, Is.EqualTo(1f));
            Assert.That(setting.IO.InputDevice.ButtonRing.Enable, Is.True);
            Assert.That(setting.IO.InputDevice.TouchPanel.CapacitivePanelOptions.RadiusOffset.E, Is.EqualTo(25));
            Assert.That(setting.IO.OutputDevice.Led.Brightness, Is.EqualTo(1f));
            Assert.That(setting.IO.InputDevice.ButtonRing.HidOptions.OpenPriority, Is.EqualTo(HidOpenPriority.VeryHigh));
#endif
#if UNITY_STANDALONE_WIN
            Assert.That(setting.Audio.Wasapi.Exclusive, Is.True);
            Assert.That(setting.Audio.Asio.SampleRate, Is.EqualTo(44100));
#endif
        }

        /// <summary>Checks that settings survive a JSON round trip and that runtime-only data is not written.</summary>
        [Test]
        public void Json_RoundTripsAndOmitsRuntimeOptions()
        {
            var setting = new GameSetting();
            setting.Game.TapSpeed = 9.25f;
            setting.Judge.AudioOffset = -1.5f;
            setting.Display.Skin = "custom";
            setting.Rendering.RenderQuality = RenderingQualityOption.Beautiful;
            setting.Rendering.FPSLimit = 144;
            setting.Debug.SensorOpacity = 40;
            setting.Mod.AllBreak = true;

            var json = Write(setting);
            var parsed = JObject.Parse(json);
            var restored = Read(json);

            Assert.That(parsed.ContainsKey("Mod"), Is.False);
            Assert.That(parsed["Display"]!.ToObject<JObject>()!.ContainsKey("RenderQuality"), Is.False);
            Assert.That(parsed["Rendering"]!["RenderQuality"]!.Value<string>(), Is.EqualTo("Beautiful"));
            Assert.That(restored.Game.TapSpeed, Is.EqualTo(9.25f));
            Assert.That(restored.Judge.AudioOffset, Is.EqualTo(-1.5f));
            Assert.That(restored.Display.Skin, Is.EqualTo("custom"));
            Assert.That(restored.Rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Beautiful));
            Assert.That(restored.Rendering.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.MSAA4x));
            Assert.That(restored.Rendering.FPSLimit, Is.EqualTo(144));
            Assert.That(restored.Debug.SensorOpacity, Is.EqualTo(40));
            Assert.That(restored.Mod.AllBreak, Is.False);
        }

        /// <summary>
        /// Checks that the slide path style is written by name, read back, and defaults to classic for a settings.json
        /// written before the option existed.
        /// </summary>
        [Test]
        public void Json_StoresTheSlidePathStyleByName()
        {
            var setting = new GameSetting();
            setting.Display.SlidePathStyle = SlidePathStyle.Medium;

            var json = Write(setting);

            Assert.That(JObject.Parse(json)["Display"]!["SlidePathStyle"]!.Value<string>(), Is.EqualTo("Medium"));
            Assert.That(Read(json).Display.SlidePathStyle, Is.EqualTo(SlidePathStyle.Medium));
            Assert.That(Read("{\"Display\":{\"Skin\":\"default\"}}").Display.SlidePathStyle, Is.EqualTo(SlidePathStyle.Classic));
        }

        /// <summary>Checks that every bundled language names the slide path style option, describes it and names its values.</summary>
        /// <param name="code">The language code.</param>
        [TestCase("en-US")]
        [TestCase("ja-JP")]
        [TestCase("zh-CN")]
        [TestCase("zh-TW")]
        public void SlidePathStyle_IsTranslatedInEveryBundledLanguage(string code)
        {
            var language = Localization.Parse(Resources.Load<TextAsset>("Langs/" + code).text)!;
            var translations = language.GetTranslations();

            Assert.That(translations.ContainsKey("MAJSETTING_PROPERTY_SlidePathStyle"), Is.True, code);
            Assert.That(translations.ContainsKey("MAJSETTING_PROPERTY_SlidePathStyle_DESC"), Is.True, code);
            foreach (var value in new[] { SlidePathStyle.Classic, SlidePathStyle.Medium, SlidePathStyle.Perfect })
            {
                Assert.That(translations.ContainsKey($"MAJSETTING_PROPERTY_SlidePathStyle_OPTION_{value}"), Is.True, $"{code} {value}");
            }
        }

        /// <summary>Checks that null setting categories fall back to their defaults instead of leaving the settings without them.</summary>
        [Test]
        public void Read_NullCategoriesUseDefaults()
        {
            var restored = Read("{\"Game\":null,\"Display\":null,\"Rendering\":null}");

            Assert.That(restored.Game, Is.Not.Null);
            Assert.That(restored.Game.TapSpeed, Is.EqualTo(7.5f));
            Assert.That(restored.Display, Is.Not.Null);
            Assert.That(restored.Display.Skin, Is.EqualTo("default"));
            Assert.That(restored.Rendering, Is.Not.Null);
            Assert.That(restored.Rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Performance));
            Assert.That(restored.Rendering.FPSLimit, Is.EqualTo(120));
        }

        /// <summary>Checks that strings resembling dates are read exactly as written.</summary>
        [Test]
        public void Read_KeepsDateLikeStrings()
        {
            var restored = Read("{\"Display\":{\"Skin\":\"2024-01-02T03:04:05Z\"}}");

            Assert.That(restored.Display.Skin, Is.EqualTo("2024-01-02T03:04:05Z"));
        }

        /// <summary>Checks that the reader rejects what the plain deserializer rejected, and returns null for the null literal.</summary>
        [Test]
        public void Read_RejectsInvalidDocuments()
        {
            Assert.That(MajEnv.DeserializeUserSettings("null"), Is.Null);
            Assert.That(MajEnv.DeserializeUserSettings(""), Is.Null);
            Assert.That(() => MajEnv.DeserializeUserSettings("[]"), Throws.InstanceOf<JsonException>());
            Assert.That(() => MajEnv.DeserializeUserSettings("{} {}"), Throws.InstanceOf<JsonException>());
            Assert.That(() => MajEnv.DeserializeUserSettings("{\"Game\":{\"TapSpeed\":\"fast\"}}"), Throws.InstanceOf<JsonException>());
        }

        /// <summary>Checks that the sensor opacity is snapped to the 10% steps between 10% and 90%.</summary>
        /// <param name="value">The assigned value.</param>
        /// <param name="expected">The stored value.</param>
        [TestCase(-5, 10)]
        [TestCase(10, 10)]
        [TestCase(14, 10)]
        [TestCase(15, 20)]
        [TestCase(26, 30)]
        [TestCase(45, 50)]
        [TestCase(84, 80)]
        [TestCase(85, 90)]
        [TestCase(90, 90)]
        [TestCase(99, 90)]
        public void SensorOpacity_SnapsToSteps(int value, int expected)
        {
            var debug = new DebugOptions();

            debug.SensorOpacity = value;

            Assert.That(debug.SensorOpacity, Is.EqualTo(expected));
        }

        /// <summary>Checks which mods count as active.</summary>
        [Test]
        public void IsAnyModActive_DetectsGameplayMods()
        {
            Assert.That(new ModOptions { PlaybackSpeed = 1.5f }.IsAnyModActive(), Is.True);
            Assert.That(new ModOptions { AllBreak = true }.IsAnyModActive(), Is.True);
            Assert.That(new ModOptions { AllEx = true }.IsAnyModActive(), Is.True);
            Assert.That(new ModOptions { AllTouch = true }.IsAnyModActive(), Is.True);
            Assert.That(new ModOptions { AutoPlay = AutoplayModeOption.Enable }.IsAnyModActive(), Is.True);
            Assert.That(new ModOptions { JudgeStyle = JudgeStyleOption.MAJI }.IsAnyModActive(), Is.True);
            Assert.That(new ModOptions { SlideNoHead = true }.IsAnyModActive(), Is.False);
            Assert.That(new ModOptions().NoteMask, Is.EqualTo("Disable"));
        }

        /// <summary>Checks the defaults of per-chart settings and runtime configuration.</summary>
        [Test]
        public void ChartSettingAndRuntimeConfig_Defaults()
        {
            var chart = new ChartSetting { Hash = "h" };
            var runtime = new RuntimeConfig();

            Assert.That(chart.Unit, Is.EqualTo(OffsetUnitOption.Second));
            Assert.That(chart.AudioOffset, Is.EqualTo(0f));
            Assert.That(chart.SlideSkipping, Is.Null);
            Assert.That(chart.DisableVideoBG, Is.False);
            Assert.That(runtime.List.SelectedDirGuid, Is.EqualTo(System.Guid.Empty));
            Assert.That(runtime.List.SelectedSongHash, Is.Empty);
            Assert.That(runtime.List.SelectedDiff, Is.EqualTo(ChartLevel.Easy));
            Assert.That(runtime.List.OrderBy.SortBy, Is.EqualTo(SortType.Default));
            Assert.That(runtime.Setting.SelectedMenu, Is.EqualTo(nameof(GameSetting.Game)));
            Assert.That(runtime.Setting.SelectedOption, Is.Empty);
            Assert.That(runtime.Setting.IgnoreChartSettingPage, Is.False);
        }

        /// <summary>Checks that the runtime configuration keeps the list selection but not the transient sort keyword.</summary>
        [Test]
        public void RuntimeConfig_JsonOmitsTransientState()
        {
            var runtime = new RuntimeConfig();
            runtime.List.SelectedDirGuid = System.Guid.NewGuid();
            runtime.List.SelectedSongHash = "abc";
            runtime.List.OrderBy.Keyword = "search";
            runtime.Setting.SelectedMenu = "Audio";

            var json = Serializer.Json.Serialize(runtime, MajEnv.UserJsonReaderOption);
            var restored = Serializer.Json.Deserialize<RuntimeConfig>(json, MajEnv.UserJsonReaderOption)!;

            Assert.That(restored.List.SelectedDirGuid, Is.EqualTo(runtime.List.SelectedDirGuid));
            Assert.That(restored.List.SelectedSongHash, Is.EqualTo("abc"));
            Assert.That(restored.List.OrderBy.Keyword, Is.Empty);
            Assert.That(restored.Setting.SelectedMenu, Is.EqualTo(nameof(GameSetting.Game)));
        }
    }
}
