using System.Linq;
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
    /// <summary>
    /// Covers the defaults, JSON round trip and file reading of <see cref="UserSetting"/>, stored in settings.json, and
    /// <see cref="SystemSetting"/>, stored in system-settings.json.
    /// </summary>
    /// <remarks>The upgrade of legacy keys is covered by <see cref="SettingMigrationTests"/>.</remarks>
    public class SettingTests
    {
        /// <summary>Serializes settings with the reader options used for the settings files.</summary>
        /// <param name="setting">The settings to write.</param>
        /// <returns>The JSON text.</returns>
        private static string Write(object setting)
        {
            return Serializer.Json.Serialize(setting, MajEnv.UserJsonReaderOption);
        }

        /// <summary>Deserializes settings the way settings.json and system-settings.json are read.</summary>
        /// <param name="userJson">The text of settings.json, or null when the file does not exist.</param>
        /// <param name="systemJson">The text of system-settings.json, or null when the file does not exist.</param>
        /// <returns>The user and system settings.</returns>
        private static (UserSetting User, SystemSetting System) Read(string? userJson, string? systemJson = null)
        {
            var (user, system) = MajEnv.DeserializeSettings(userJson, systemJson);
            Assert.That(user, Is.Not.Null);
            Assert.That(system, Is.Not.Null);
            return (user!, system!);
        }

        /// <summary>Checks the defaults that the game relies on during first startup.</summary>
        [Test]
        public void Defaults_MatchFirstStartup()
        {
            var user = new UserSetting();
            var system = new SystemSetting();

            Assert.That(user.Game.TapSpeed, Is.EqualTo(7.5f));
            Assert.That(user.Game.LeadInTime, Is.EqualTo(1));
            Assert.That(user.Game.NoteAppearRate, Is.EqualTo(0.265f));
            Assert.That(user.Judge.Mode, Is.EqualTo(JudgeModeOption.Modern));
            Assert.That(user.Judge.OffsetUnit, Is.EqualTo(OffsetUnitOption.Frame));
            Assert.That(user.Judge.DisplayBreakScore, Is.True);
            Assert.That(user.Display.Skin, Is.EqualTo("default"));
            Assert.That(user.Display.Language, Is.Empty);
            Assert.That(user.Display.SlidePathStyle, Is.EqualTo(SlidePathStyle.Classic));
            Assert.That(user.Display.BGInfo, Is.EqualTo(BGInfoOption.Combo));
            Assert.That(user.Display.DisplaySensor, Is.False);
            Assert.That(user.Volume.Global, Is.EqualTo(0.3f));
            Assert.That(user.Touch.TouchSimulationRadius, Is.EqualTo(0.5f));
            Assert.That(user.Touch.TouchCAreaExtraRadius, Is.EqualTo(0.25f));
            Assert.That(system.Rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Performance));
            Assert.That(system.Rendering.DisplayRuntimeInfo, Is.True);
            Assert.That(system.Audio.Bass.BufferLengthMs, Is.EqualTo(1000));
            Assert.That(system.Debug.DebugLevel, Is.EqualTo(LogLevel.Info));
            Assert.That(system.Online.Enable, Is.False);
            Assert.That(system.Mod.IsAnyModActive(), Is.False);
#if UNITY_ANDROID || UNITY_IOS
            Assert.That(user.Game.GameplaySubScreenClickBehavior, Is.EqualTo(GameplaySubScreenClickBehaviorOption.TrackSkip_1_Sec_Delay));
            Assert.That(user.Game.ButtonRingForTouch, Is.True);
            Assert.That(system.Rendering.MainScreenTransform, Is.True);
            Assert.That(system.Audio.Backend, Is.EqualTo(SoundBackendOption.BassSimple));
            Assert.That(system.Audio.Bass.DeviceBufferLengthMs, Is.EqualTo(32));
            Assert.That(system.Audio.Bass.DeviceUpdatePeriodMs, Is.EqualTo(8));
            Assert.That(system.Debug.TapPoolCapacity, Is.EqualTo(48));
#else
            Assert.That(user.Game.GameplaySubScreenClickBehavior, Is.EqualTo(GameplaySubScreenClickBehaviorOption.TrackSkip));
            Assert.That(user.Game.ButtonRingForTouch, Is.False);
            Assert.That(system.Rendering.MainScreenTransform, Is.False);
            Assert.That(system.Audio.Backend, Is.EqualTo(SoundBackendOption.Wasapi));
            Assert.That(system.Audio.Bass.DeviceBufferLengthMs, Is.EqualTo(64));
            Assert.That(system.Audio.Bass.DeviceUpdatePeriodMs, Is.EqualTo(16));
            Assert.That(system.Debug.TapPoolCapacity, Is.EqualTo(96));
#endif
#if UNITY_IOS
            Assert.That(system.Online.ApiEndpoints, Is.Empty);
#else
            Assert.That(system.Online.ApiEndpoints, Has.Length.EqualTo(1));
            Assert.That(system.Online.ApiEndpoints[0].Name, Is.EqualTo("MajdataNET"));
#endif
#if UNITY_STANDALONE
            Assert.That(system.Rendering.Resolution, Is.EqualTo("1080x1920"));
            Assert.That(system.Rendering.FullScreen, Is.True);
            Assert.That(system.Audio.Channel.FrontVolume, Is.EqualTo(1f));
            Assert.That(system.IO.InputDevice.ButtonRing.Enable, Is.True);
            Assert.That(system.IO.InputDevice.TouchPanel.CapacitivePanelOptions.RadiusOffset.E, Is.EqualTo(25));
            Assert.That(system.IO.OutputDevice.Led.Brightness, Is.EqualTo(1f));
            Assert.That(system.IO.InputDevice.ButtonRing.HidOptions.OpenPriority, Is.EqualTo(HidOpenPriority.VeryHigh));
#endif
#if UNITY_STANDALONE_WIN
            Assert.That(system.Audio.Wasapi.Exclusive, Is.True);
            Assert.That(system.Audio.Asio.SampleRate, Is.EqualTo(44100));
#endif
        }

        /// <summary>
        /// Checks that both settings survive a JSON round trip, that each file only holds its own categories and that
        /// runtime-only data is not written.
        /// </summary>
        [Test]
        public void Json_RoundTripsAndOmitsRuntimeOptions()
        {
            var user = new UserSetting();
            var system = new SystemSetting();
            user.Game.TapSpeed = 9.25f;
            user.Game.ButtonRingForTouch = true;
            user.Judge.AudioOffset = -1.5f;
            user.Display.Skin = "custom";
            user.Display.SensorOpacity = 40;
            user.Touch.TouchRadiusAdjust = 0.35f;
            system.Rendering.RenderQuality = RenderingQualityOption.Beautiful;
            system.Rendering.FPSLimit = 144;
            system.Mod.AllBreak = true;

            var userJson = Write(user);
            var systemJson = Write(system);
            var parsedUser = JObject.Parse(userJson);
            var parsedSystem = JObject.Parse(systemJson);
            var (restoredUser, restoredSystem) = Read(userJson, systemJson);

            Assert.That(parsedUser.Properties().Select(category => category.Name),
                Is.EqualTo(new[] { "Game", "Judge", "Display", "Volume", "Touch" }));
            Assert.That(parsedSystem.Properties().Select(category => category.Name),
                Is.EqualTo(new[] { "Rendering", "Audio", "Encoding", "Decoding", "Debug", "Online", "IO" }));
            Assert.That(((JObject)parsedSystem["Audio"]!).ContainsKey("Volume"), Is.False);
            Assert.That(parsedSystem["Rendering"]!["RenderQuality"]!.Value<string>(), Is.EqualTo("Beautiful"));
            Assert.That(restoredUser.Game.TapSpeed, Is.EqualTo(9.25f));
            Assert.That(restoredUser.Game.ButtonRingForTouch, Is.True);
            Assert.That(restoredUser.Judge.AudioOffset, Is.EqualTo(-1.5f));
            Assert.That(restoredUser.Display.Skin, Is.EqualTo("custom"));
            Assert.That(restoredUser.Display.SensorOpacity, Is.EqualTo(40));
            Assert.That(restoredUser.Touch.TouchRadiusAdjust, Is.EqualTo(0.35f));
            Assert.That(restoredSystem.Rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Beautiful));
            Assert.That(restoredSystem.Rendering.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.MSAA4x));
            Assert.That(restoredSystem.Rendering.FPSLimit, Is.EqualTo(144));
            Assert.That(restoredSystem.Mod.AllBreak, Is.False);
        }

        /// <summary>
        /// Checks that the slide path style is written by name, read back, and defaults to classic for a settings.json
        /// written before the option existed.
        /// </summary>
        [Test]
        public void Json_StoresTheSlidePathStyleByName()
        {
            var setting = new UserSetting();
            setting.Display.SlidePathStyle = SlidePathStyle.Medium;

            var json = Write(setting);

            Assert.That(JObject.Parse(json)["Display"]!["SlidePathStyle"]!.Value<string>(), Is.EqualTo("Medium"));
            Assert.That(Read(json).User.Display.SlidePathStyle, Is.EqualTo(SlidePathStyle.Medium));
            Assert.That(Read("{\"Display\":{\"Skin\":\"default\"}}").User.Display.SlidePathStyle, Is.EqualTo(SlidePathStyle.Classic));
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

        /// <summary>Checks that every bundled language names the Touch menu, which holds the simulated touch radii.</summary>
        /// <param name="code">The language code.</param>
        [TestCase("en-US")]
        [TestCase("ja-JP")]
        [TestCase("zh-CN")]
        [TestCase("zh-TW")]
        public void TouchMenu_IsTranslatedInEveryBundledLanguage(string code)
        {
            var language = Localization.Parse(Resources.Load<TextAsset>("Langs/" + code).text)!;

            Assert.That(language.GetTranslations().ContainsKey("MAJSETTING_CATEGORY_Touch"), Is.True, code);
        }

        /// <summary>Checks that missing files read as defaults.</summary>
        [Test]
        public void Read_MissingFilesUseDefaults()
        {
            var (user, system) = Read(null, null);

            Assert.That(user.Game.TapSpeed, Is.EqualTo(7.5f));
            Assert.That(system.Rendering.FPSLimit, Is.EqualTo(120));
        }

        /// <summary>Checks that null setting categories fall back to their defaults instead of leaving the settings without them.</summary>
        [Test]
        public void Read_NullCategoriesUseDefaults()
        {
            var (user, system) = Read("{\"Game\":null,\"Display\":null,\"Touch\":null}", "{\"Rendering\":null,\"Debug\":null}");

            Assert.That(user.Game, Is.Not.Null);
            Assert.That(user.Game.TapSpeed, Is.EqualTo(7.5f));
            Assert.That(user.Display, Is.Not.Null);
            Assert.That(user.Display.Skin, Is.EqualTo("default"));
            Assert.That(user.Touch, Is.Not.Null);
            Assert.That(system.Rendering, Is.Not.Null);
            Assert.That(system.Rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Performance));
            Assert.That(system.Rendering.FPSLimit, Is.EqualTo(120));
            Assert.That(system.Debug, Is.Not.Null);
        }

        /// <summary>Checks that strings resembling dates are read exactly as written.</summary>
        [Test]
        public void Read_KeepsDateLikeStrings()
        {
            var (user, system) = Read("{\"Display\":{\"Skin\":\"2024-01-02T03:04:05Z\"}}",
                "{\"Online\":{\"ApiEndpoints\":[{\"Name\":\"n\",\"Url\":\"https://example.invalid/\",\"Password\":\"2024-01-02T03:04:05Z\"}]}}");

            Assert.That(user.Display.Skin, Is.EqualTo("2024-01-02T03:04:05Z"));
            Assert.That(system.Online.ApiEndpoints[0].Password, Is.EqualTo("2024-01-02T03:04:05Z"));
        }

        /// <summary>Checks that the reader rejects what the plain deserializer rejected, and returns null for the null literal.</summary>
        [Test]
        public void Read_RejectsInvalidDocuments()
        {
            Assert.That(MajEnv.DeserializeSettings("null", null).User, Is.Null);
            Assert.That(MajEnv.DeserializeSettings("", null).User, Is.Null);
            Assert.That(MajEnv.DeserializeSettings(null, "null").System, Is.Null);
            Assert.That(MajEnv.DeserializeSettings(null, "").System, Is.Null);
            Assert.That(() => MajEnv.DeserializeSettings("[]", null), Throws.InstanceOf<JsonException>());
            Assert.That(() => MajEnv.DeserializeSettings(null, "[]"), Throws.InstanceOf<JsonException>());
            Assert.That(() => MajEnv.DeserializeSettings("{} {}", null), Throws.InstanceOf<JsonException>());
            Assert.That(() => MajEnv.DeserializeSettings("{\"Game\":{\"TapSpeed\":\"fast\"}}", null), Throws.InstanceOf<JsonException>());
            Assert.That(() => MajEnv.DeserializeSettings(null, "{\"Debug\":{\"MaxQueuedFrames\":\"many\"}}"), Throws.InstanceOf<JsonException>());
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
            var display = new DisplayOptions();

            display.SensorOpacity = value;

            Assert.That(display.SensorOpacity, Is.EqualTo(expected));
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
            Assert.That(runtime.List.SelectedSongId, Is.Empty);
            Assert.That(runtime.List.SelectedDiff, Is.EqualTo(ChartLevel.Easy));
            Assert.That(runtime.List.OrderBy.SortBy, Is.EqualTo(SortType.Default));
            Assert.That(runtime.Setting.SelectedMenu, Is.EqualTo(nameof(UserSetting.Game)));
            Assert.That(runtime.Setting.SelectedOption, Is.Empty);
            Assert.That(runtime.Setting.IgnoreChartSettingPage, Is.False);
        }

        /// <summary>Checks that the runtime configuration keeps the list selection but not the transient sort keyword.</summary>
        [Test]
        public void RuntimeConfig_JsonOmitsTransientState()
        {
            var runtime = new RuntimeConfig();
            runtime.List.SelectedDirGuid = System.Guid.NewGuid();
            runtime.List.SelectedSongId = "abc";
            runtime.List.OrderBy.Keyword = "search";
            runtime.Setting.SelectedMenu = "Audio";

            var json = Serializer.Json.Serialize(runtime, MajEnv.UserJsonReaderOption);
            var restored = Serializer.Json.Deserialize<RuntimeConfig>(json, MajEnv.UserJsonReaderOption)!;

            Assert.That(restored.List.SelectedDirGuid, Is.EqualTo(runtime.List.SelectedDirGuid));
            Assert.That(restored.List.SelectedSongId, Is.EqualTo("abc"));
            Assert.That(restored.List.OrderBy.Keyword, Is.Empty);
            Assert.That(restored.Setting.SelectedMenu, Is.EqualTo(nameof(UserSetting.Game)));
        }
    }
}
