using MajdataPlay.Diagnostics;
using MajdataPlay.Settings;
using MajdataPlay.Settings.Runtime;
using MajdataPlay.Utils;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Settings
{
    /// <summary>Covers the defaults, JSON round trip and legacy migration of <see cref="GameSetting"/>.</summary>
    public class GameSettingTests
    {
        /// <summary>Serializes settings with the reader options used for settings.json.</summary>
        /// <param name="setting">The settings to write.</param>
        /// <returns>The JSON text.</returns>
        private static string Write(GameSetting setting)
        {
            return Serializer.Json.Serialize(setting, MajEnv.UserJsonReaderOption);
        }

        /// <summary>Deserializes settings with the reader options used for settings.json.</summary>
        /// <param name="json">The JSON text.</param>
        /// <returns>The settings.</returns>
        private static GameSetting Read(string json)
        {
            var setting = Serializer.Json.Deserialize<GameSetting>(json, MajEnv.UserJsonReaderOption);
            Assert.That(setting, Is.Not.Null);
            return setting!;
        }

        /// <summary>Checks the defaults that the game relies on during first startup.</summary>
        [Test]
        public void Defaults_MatchFirstStartup()
        {
            var setting = new GameSetting();

            Assert.That(setting.Game.TapSpeed, Is.EqualTo(7.5f));
            Assert.That(setting.Game.GameplaySubScreenClickBehavior, Is.EqualTo(GameplaySubScreenClickBehaviorOption.TrackSkip));
            Assert.That(setting.Game.LeadInTime, Is.EqualTo(1));
            Assert.That(setting.Judge.Mode, Is.EqualTo(JudgeModeOption.Modern));
            Assert.That(setting.Display.Skin, Is.EqualTo("default"));
            Assert.That(setting.Display.Language, Is.Empty);
            Assert.That(setting.Display.MainScreenTransform, Is.False);
            Assert.That(setting.Display.Resolution, Is.EqualTo("1080x1920"));
            Assert.That(setting.Rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Performance));
            Assert.That(setting.Audio.Backend, Is.EqualTo(SoundBackendOption.Wasapi));
            Assert.That(setting.Audio.Bass.DeviceBufferLengthMs, Is.EqualTo(64));
            Assert.That(setting.Audio.Bass.DeviceUpdatePeriodMs, Is.EqualTo(16));
            Assert.That(setting.Audio.Bass.BufferLengthMs, Is.EqualTo(1000));
            Assert.That(setting.Audio.Wasapi.Exclusive, Is.True);
            Assert.That(setting.Audio.Asio.SampleRate, Is.EqualTo(44100));
            Assert.That(setting.Audio.Channel.FrontVolume, Is.EqualTo(1f));
            Assert.That(setting.Audio.Volume.Global, Is.EqualTo(0.3f));
            Assert.That(setting.Debug.OffsetUnit, Is.EqualTo(OffsetUnitOption.Frame));
            Assert.That(setting.Debug.DebugLevel, Is.EqualTo(LogLevel.Info));
            Assert.That(setting.Debug.TapPoolCapacity, Is.EqualTo(96));
            Assert.That(setting.Online.Enable, Is.False);
            Assert.That(setting.Online.ApiEndpoints, Has.Length.EqualTo(1));
            Assert.That(setting.Online.ApiEndpoints[0].Name, Is.EqualTo("MajdataNET"));
            Assert.That(setting.IO.InputDevice.ButtonRing.Enable, Is.True);
            Assert.That(setting.IO.InputDevice.TouchPanel.CapacitivePanelOptions.RadiusOffset.E, Is.EqualTo(25));
            Assert.That(setting.IO.OutputDevice.Led.Brightness, Is.EqualTo(1f));
            Assert.That(setting.IO.InputDevice.ButtonRing.HidOptions.OpenPriority, Is.EqualTo(HidOpenPriority.VeryHigh));
            Assert.That(setting.Mod.IsAnyModActive(), Is.False);
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

        /// <summary>Checks that legacy rendering keys stored under Display are migrated when no Rendering section exists.</summary>
        [Test]
        public void Json_MigratesLegacyDisplayRendering()
        {
            var restored = Read("{\"Display\":{\"RenderQuality\":\"High\",\"RenderScale\":100,\"FPSLimit\":60,\"VSync\":false}}");

            Assert.That(restored.Rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Beautiful));
            Assert.That(restored.Rendering.AntiAliasing, Is.EqualTo(RenderingAntialiasingOption.MSAA4x));
            Assert.That(restored.Rendering.FPSLimit, Is.EqualTo(60));
            Assert.That(restored.Rendering.VSync, Is.False);
            Assert.That(restored.Display.LegacyFPSLimit, Is.EqualTo(60));
            Assert.That(restored.Display.LegacyVSync, Is.False);
        }

        /// <summary>Checks that an existing Rendering section wins over legacy keys, including the frame pacing it stores.</summary>
        [Test]
        public void Json_ExistingRenderingWinsOverLegacy()
        {
            var restored = Read("{\"Display\":{\"RenderQuality\":\"High\",\"FPSLimit\":60,\"VSync\":false},\"Rendering\":{\"RenderQuality\":\"Refined\",\"FPSLimit\":90,\"VSync\":true}}");

            Assert.That(restored.Rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Refined));
            Assert.That(restored.Rendering.RenderScale, Is.EqualTo(150));
            Assert.That(restored.Rendering.FPSLimit, Is.EqualTo(90));
            Assert.That(restored.Rendering.VSync, Is.True);
        }

        /// <summary>Checks that legacy frame pacing fills in keys missing from a newer Rendering section.</summary>
        [Test]
        public void Json_LegacyFramePacingFillsMissingKeys()
        {
            var restored = Read("{\"Display\":{\"FPSLimit\":60,\"VSync\":false},\"Rendering\":{\"RenderQuality\":\"EnergySaving\"}}");

            Assert.That(restored.Rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.EnergySaving));
            Assert.That(restored.Rendering.TextureQuality, Is.EqualTo(RenderingTextureQualityOption.Medium));
            Assert.That(restored.Rendering.FPSLimit, Is.EqualTo(60));
            Assert.That(restored.Rendering.VSync, Is.False);
        }

        /// <summary>Checks that a null Rendering section falls back to the defaults.</summary>
        [Test]
        public void Json_NullRenderingUsesDefaults()
        {
            var restored = Read("{\"Rendering\":null}");

            Assert.That(restored.Rendering, Is.Not.Null);
            Assert.That(restored.Rendering.RenderQuality, Is.EqualTo(RenderingQualityOption.Performance));
            Assert.That(restored.Rendering.FPSLimit, Is.EqualTo(120));
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
            runtime.List.SelectedSongIndex = 4;
            runtime.List.SelectedSongHash = "abc";
            runtime.List.OrderBy.Keyword = "search";
            runtime.Setting.SelectedMenu = "Audio";

            var json = Serializer.Json.Serialize(runtime, MajEnv.UserJsonReaderOption);
            var restored = Serializer.Json.Deserialize<RuntimeConfig>(json, MajEnv.UserJsonReaderOption)!;

            Assert.That(restored.List.SelectedSongIndex, Is.EqualTo(4));
            Assert.That(restored.List.SelectedSongHash, Is.EqualTo("abc"));
            Assert.That(restored.List.OrderBy.Keyword, Is.Empty);
            Assert.That(restored.Setting.SelectedMenu, Is.EqualTo(nameof(GameSetting.Game)));
        }
    }
}
