#if UNITY_STANDALONE
using System;
using System.Collections.Generic;
using MajdataPlay.IO;
using MajdataPlay.Settings;
using MajdataPlay.Timer;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>Verifies the LED effect functions and frame output of the static <see cref="CabinetLed"/>.</summary>
    public class CabinetLedTests
    {
        /// <summary>The static array fields of <see cref="CabinetLed"/> restored after each test.</summary>
        private static readonly string[] s_arrayFields = { "_ledColors", "_ledRingDevices", "_ledCommFuncs", "_ledLinearFuncs", "_ledSineFuncs" };

        /// <summary>The nested LED facade type of the output manager.</summary>
        private static readonly Type s_ledDeviceType = typeof(OutputManager).GetNestedType("LedDevice", System.Reflection.BindingFlags.NonPublic)!;

        /// <summary>The copies of the static arrays before the test.</summary>
        private readonly Dictionary<string, Array> _savedArrays = new();

        /// <summary>The cabinet light before the test.</summary>
        private object? _savedCabinetLight;

        /// <summary>The enabled flag before the test.</summary>
        private bool _savedEnabled;

        /// <summary>The last update time before the test.</summary>
        private TimeSpan _savedLastUpdate;

        /// <summary>The output device before the test.</summary>
        private object? _savedOutputDevice;

        /// <summary>The time provider before the test.</summary>
        private ITimeProvider _savedTimeProvider = null!;

        /// <summary>The installed default settings.</summary>
        private SettingsScope _settings = null!;

        /// <summary>The manual clock driving the effects.</summary>
        private ManualTimeProvider _clock = null!;

        /// <summary>The scripted device receiving the output.</summary>
        private FakeInputDevice _output = null!;

        /// <summary>Saves the static state and installs the clock, settings and output device.</summary>
        [SetUp]
        public void SetUp()
        {
            foreach (var name in s_arrayFields)
            {
                _savedArrays[name] = (Array)PrivateMember.GetStaticField<Array>(typeof(CabinetLed), name).Clone();
            }
            _savedCabinetLight = PrivateMember.GetStaticField<object?>(typeof(CabinetLed), "_cabinetLight");
            _savedEnabled = CabinetLed.IsEnabled;
            _savedLastUpdate = PrivateMember.GetStaticField<TimeSpan>(typeof(CabinetLed), "_lastUpdateTime");
            _savedOutputDevice = PrivateMember.GetStaticField<object?>(s_ledDeviceType, "_device");
            _savedTimeProvider = MajTimeline.TimeProvider;

            _settings = new SettingsScope();
            _clock = new ManualTimeProvider { Ticks = TimeSpan.FromSeconds(10).Ticks };
            MajTimeline.TimeProvider = _clock;
            _output = new FakeInputDevice();
            PrivateMember.SetStaticField(s_ledDeviceType, "_device", _output);
        }

        /// <summary>Restores the static state.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (var pair in _savedArrays)
            {
                Array.Copy(pair.Value, PrivateMember.GetStaticField<Array>(typeof(CabinetLed), pair.Key), pair.Value.Length);
            }
            PrivateMember.SetStaticField(typeof(CabinetLed), "_cabinetLight", _savedCabinetLight);
            PrivateMember.SetStaticProperty(typeof(CabinetLed), nameof(CabinetLed.IsEnabled), _savedEnabled);
            PrivateMember.SetStaticField(typeof(CabinetLed), "_lastUpdateTime", _savedLastUpdate);
            PrivateMember.SetStaticField(s_ledDeviceType, "_device", _savedOutputDevice);
            MajTimeline.TimeProvider = _savedTimeProvider;
            _settings.Dispose();
        }

        /// <summary>Checks that a disabled cabinet stays dark and ignores every effect request.</summary>
        [Test]
        public void Disabled_StaysDarkAndIgnoresEffects()
        {
            Init(false);

            CabinetLed.SetAllLight(Color.red);
            CabinetLed.SetButtonLight(Color.green, 1);
            CabinetLed.SetButtonLightWithTimeout(Color.green, 1, 100);
            CabinetLed.SetButtonLightWithTimeout(Color.green, 1, TimeSpan.FromSeconds(1));
            CabinetLed.LinearTo(2, Color.black, Color.white, 100);
            CabinetLed.SetSineFunc(3, Color.blue, 100);
            CabinetLed.SetUpdateFunc(4, new ConstantLed(Color.cyan));
            CabinetLed.SetAllLightLinearTo(Color.black, Color.white, 100);
            CabinetLed.SetAllLightSineFunc(Color.white, 100);
            CabinetLed.SetAllLightUpdateFunc(Array.Empty<ILedUpdateFunction>());
            CabinetLed.SetCabinetLight(1f);
            CabinetLed.SetCabinetLight((byte)255);
            CabinetLed.SetCabinetLightSineFunc(1f, 100);
            Pump(TimeSpan.FromMilliseconds(16));

            Assert.That(CabinetLed.IsEnabled, Is.False);
            Assert.That(_output.LastColors, Is.All.EqualTo(Color.black));
            Assert.That(_output.LastCabinetBrightness, Is.EqualTo(0));
        }

        /// <summary>Checks that an enabled cabinet starts white and sends every frame to the output and the dummy renderer.</summary>
        [Test]
        public void Enabled_StartsWhiteAndPublishesFrames()
        {
            Init(true);

            Pump(TimeSpan.FromMilliseconds(16));

            Assert.That(CabinetLed.IsEnabled, Is.True);
            Assert.That(_output.LastColors, Is.All.EqualTo(Color.white));
            Assert.That(_output.LastCabinetBrightness, Is.EqualTo(255));
            var dummy = PrivateMember.GetStaticField<Color[]>(typeof(DummyLedRenderer), "_ledRingColors");
            Assert.That(dummy, Is.All.EqualTo(Color.white));
        }

        /// <summary>Checks the solid colour effects for every button and for a single button.</summary>
        [Test]
        public void SolidColours_ApplyToAllOrOneButton()
        {
            Init(true);

            CabinetLed.SetAllLight(Color.red);
            CabinetLed.SetButtonLight(Color.green, 2);
            Pump(TimeSpan.Zero);

            Assert.That(_output.LastColors[0], Is.EqualTo(Color.red));
            Assert.That(_output.LastColors[2], Is.EqualTo(Color.green));
        }

        /// <summary>Checks that a timed colour lasts for its duration and then returns to the default colour.</summary>
        [Test]
        public void TimedColour_ReturnsToDefaultAfterTheDuration()
        {
            Init(true);
            CabinetLed.SetAllLight(Color.red);
            CabinetLed.SetButtonLightWithTimeout(Color.blue, 1, 500);
            CabinetLed.SetButtonLightWithTimeout(Color.yellow, 5, TimeSpan.FromMilliseconds(500));

            Pump(TimeSpan.FromMilliseconds(100));
            Assert.That(_output.LastColors[1], Is.EqualTo(Color.blue));
            Assert.That(_output.LastColors[5], Is.EqualTo(Color.yellow));

            Pump(TimeSpan.FromMilliseconds(600));
            Assert.That(_output.LastColors[1], Is.EqualTo(Color.blue), "the elapsed time is checked before it is advanced");

            Pump(TimeSpan.FromMilliseconds(16));
            Assert.That(_output.LastColors[1], Is.EqualTo(Color.red));
            Assert.That(_output.LastColors[5], Is.EqualTo(Color.red));
        }

        /// <summary>Checks that a linear fade interpolates from its start to its end colour.</summary>
        [Test]
        public void LinearFade_Interpolates()
        {
            Init(true);
            Pump(TimeSpan.Zero);
            CabinetLed.LinearTo(3, Color.black, Color.white, 1000);

            Pump(TimeSpan.Zero);
            Assert.That(_output.LastColors[3], Is.EqualTo(Color.black));
            Pump(TimeSpan.FromMilliseconds(500));
            Pump(TimeSpan.Zero);
            AssertColor(_output.LastColors[3], new Color(0.5f, 0.5f, 0.5f, 1f));
            Pump(TimeSpan.FromMilliseconds(600));
            Pump(TimeSpan.Zero);
            Assert.That(_output.LastColors[3], Is.EqualTo(Color.white));
        }

        /// <summary>Checks that the all-button fade affects every button.</summary>
        [Test]
        public void AllButtonFade_AffectsEveryButton()
        {
            Init(true);
            Pump(TimeSpan.Zero);

            CabinetLed.SetAllLightLinearTo(Color.white, Color.black, 200);
            Pump(TimeSpan.FromMilliseconds(300));
            Pump(TimeSpan.Zero);

            Assert.That(_output.LastColors, Is.All.EqualTo(Color.black));
        }

        /// <summary>Checks that starting a second fade animates again instead of jumping to its end colour.</summary>
        [Test]
        public void SecondFade_AnimatesFromItsStart()
        {
            Init(true);
            Pump(TimeSpan.Zero);
            CabinetLed.LinearTo(0, Color.black, Color.white, 100);
            Pump(TimeSpan.FromMilliseconds(200));
            Pump(TimeSpan.Zero);

            CabinetLed.LinearTo(0, Color.red, Color.blue, 100);
            Pump(TimeSpan.Zero);

            Assert.That(_output.LastColors[0], Is.EqualTo(Color.red));
        }

        /// <summary>Checks that a pulse starts at full brightness for the default phase and follows a sine curve.</summary>
        [Test]
        public void Pulse_FollowsASineCurve()
        {
            Init(true);
            Pump(TimeSpan.Zero);
            CabinetLed.SetSineFunc(6, Color.white, 1000);

            Pump(TimeSpan.Zero);
            AssertColor(_output.LastColors[6], Color.white);
            Pump(TimeSpan.FromMilliseconds(250));
            Pump(TimeSpan.Zero);
            var expected = Mathf.Abs(Mathf.Sin(0.75f * Mathf.PI));
            AssertColor(_output.LastColors[6], new Color(expected, expected, expected, 1f));
        }

        /// <summary>Checks that a full phase wraps to the start of the period, where the pulse is dark.</summary>
        [Test]
        public void Pulse_FullPhaseWrapsToDark()
        {
            Init(true);
            Pump(TimeSpan.Zero);
            CabinetLed.SetSineFunc(7, Color.white, TimeSpan.FromMilliseconds(400), 1f);
            CabinetLed.SetAllLightSineFunc(Color.white, 400, 0f);

            Pump(TimeSpan.Zero);

            AssertColor(_output.LastColors[7], Color.black);
            AssertColor(_output.LastColors[0], Color.black);
        }

        /// <summary>Checks that invalid pulse parameters are rejected.</summary>
        [Test]
        public void Pulse_InvalidParameters_Throw()
        {
            Init(true);

            Assert.Throws<ArgumentOutOfRangeException>(() => CabinetLed.SetSineFunc(0, Color.white, 100, 1.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => CabinetLed.SetSineFunc(0, Color.white, 100, -0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => CabinetLed.SetSineFunc(0, Color.white, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => CabinetLed.SetCabinetLightSineFunc(1f, -5));
        }

        /// <summary>Checks the cabinet light brightness settings, including the sine pulse.</summary>
        [Test]
        public void CabinetLight_SetsBrightness()
        {
            Init(true);

            CabinetLed.SetCabinetLight(0.5f);
            Pump(TimeSpan.Zero);
            Assert.That((int)_output.LastCabinetBrightness, Is.InRange(127, 128));

            CabinetLed.SetCabinetLight((byte)0);
            Pump(TimeSpan.Zero);
            Assert.That(_output.LastCabinetBrightness, Is.EqualTo(0));

            CabinetLed.SetCabinetLightSineFunc(2f, 1000);
            Pump(TimeSpan.Zero);
            Assert.That(_output.LastCabinetBrightness, Is.EqualTo(255));
        }

        /// <summary>Checks that custom update functions receive the frame time and drive the output.</summary>
        [Test]
        public void CustomFunctions_ReceiveFrameTime()
        {
            Init(true);
            Pump(TimeSpan.Zero);
            var custom = new ConstantLed(Color.cyan);

            CabinetLed.SetUpdateFunc(4, custom);
            Pump(TimeSpan.FromMilliseconds(40));

            Assert.That(_output.LastColors[4], Is.EqualTo(Color.cyan));
            Assert.That(custom.LastDeltaMs, Is.EqualTo(40f).Within(0.01f));
            Assert.Throws<ArgumentNullException>(() => CabinetLed.SetUpdateFunc(4, null!));
        }

        /// <summary>Checks that the bulk custom function setter applies the first eight functions of a longer list.</summary>
        [Test]
        public void SetAllLightUpdateFunc_AppliesTheFirstEightFunctions()
        {
            Init(true);
            var functions = new ILedUpdateFunction[9];
            for (var i = 0; i < functions.Length; i++)
            {
                functions[i] = new ConstantLed(new Color(i / 10f, 0f, 0f, 1f));
            }

            CabinetLed.SetAllLightUpdateFunc(functions);
            Pump(TimeSpan.Zero);

            AssertColor(_output.LastColors[7], new Color(0.7f, 0f, 0f, 1f));
        }

        /// <summary>Checks that exactly eight functions, one per button, are accepted.</summary>
        [Test]
        public void SetAllLightUpdateFunc_AcceptsEightFunctions()
        {
            Init(true);
            var functions = new ILedUpdateFunction[8];
            for (var i = 0; i < functions.Length; i++)
            {
                functions[i] = new ConstantLed(Color.gray);
            }

            Assert.DoesNotThrow(() => CabinetLed.SetAllLightUpdateFunc(functions));
        }

        /// <summary>Checks that fewer than nine functions are currently rejected.</summary>
        [Test]
        public void SetAllLightUpdateFunc_ShortList_Throws()
        {
            Init(true);

            Assert.Throws<ArgumentException>(() => CabinetLed.SetAllLightUpdateFunc(new ILedUpdateFunction[3]));
        }

        /// <summary>Initialises the cabinet with LED output enabled or disabled.</summary>
        /// <param name="enabled">Whether LED output is enabled in the settings.</param>
        private void Init(bool enabled)
        {
            _settings.Settings.IO.OutputDevice.Led = new LedOptions { Enable = enabled };
            PrivateMember.SetStaticField(typeof(CabinetLed), "_lastUpdateTime", MajTimeline.UnscaledTime);
        }

        /// <summary>Advances the clock and runs one LED frame.</summary>
        /// <param name="elapsed">The time to advance first.</param>
        private void Pump(TimeSpan elapsed)
        {
            _clock.Advance(elapsed);
            CabinetLed.OnLateUpdate();
        }

        /// <summary>Asserts that two colours match within a small tolerance.</summary>
        /// <param name="actual">The observed colour.</param>
        /// <param name="expected">The expected colour.</param>
        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.01f), "r");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.01f), "g");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.01f), "b");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.01f), "a");
        }

        /// <summary>An LED effect with a fixed colour that records the frame time it receives.</summary>
        private sealed class ConstantLed : ILedUpdateFunction
        {
            /// <summary>Creates the effect.</summary>
            /// <param name="color">The colour reported every frame.</param>
            public ConstantLed(Color color)
            {
                Current = color;
            }

            /// <summary>Gets the reported colour.</summary>
            public Color Current { get; }

            /// <summary>Gets the frame time of the last update in milliseconds.</summary>
            public float LastDeltaMs { get; private set; }

            /// <summary>Records the frame time.</summary>
            /// <param name="deltaMs">The frame time in milliseconds.</param>
            public void Update(float deltaMs)
            {
                LastDeltaMs = deltaMs;
            }

            /// <summary>Clears the recorded frame time.</summary>
            public void Reset()
            {
                LastDeltaMs = 0f;
            }
        }
    }
}
#endif
