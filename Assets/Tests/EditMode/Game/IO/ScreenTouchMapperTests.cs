using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MajdataPlay.IO;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>
    /// Verifies the screen touch geometry of <see cref="ScreenTouchMapper"/> and the pointer aggregation of
    /// <see cref="PointerInputState"/> with an orthographic camera rendering into a fixed-size texture.
    /// </summary>
    public class ScreenTouchMapperTests
    {
        /// <summary>The sensor index whose collider is placed in the scene.</summary>
        private const int MappedSensor = 5;

        /// <summary>The sensor index written directly into the position samples.</summary>
        private const int WrittenSensor = 10;

        /// <summary>The objects created for the fixture.</summary>
        private TestObjects _objects = null!;

        /// <summary>The installed default settings.</summary>
        private SettingsScope _settings = null!;

        /// <summary>The camera used to convert screen positions.</summary>
        private Camera _camera = null!;

        /// <summary>The overrides and switches before the fixture.</summary>
        private (float? Radius, bool OuterAsSensor, bool Enhancement, float Edge, Vector4 SubScreen) _saved;

        /// <summary>Places a mapped collider, initializes the mapper and creates the camera.</summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Assert.That(ScreenTouchMapper.IsInitialized, Is.False, "another test left the mapper initialized");
            _objects = new TestObjects();
            _settings = new SettingsScope();
            _saved = (ScreenTouchMapper.Override_TouchSimulationRadius, ScreenTouchMapper.UseOuterTouchAsSensor,
                ScreenTouchMapper.UseGameplayTouchEnhancementFeatures, ScreenTouchMapper.TouchButtonRingEdge, ScreenTouchMapper.SubScreenEdge);
            ScreenTouchMapper.Override_TouchSimulationRadius = 0.1f;
            ScreenTouchMapper.TouchButtonRingEdge = 5.4f;

            var mapped = _objects.Create("MappedSensor");
            mapped.transform.position = new Vector3(1f, 1f, 0f);
            var mappedCollider = mapped.AddComponent<BoxCollider>();
            var unknown = _objects.Create("UnknownSensor");
            unknown.transform.position = new Vector3(-3f, -3f, 0f);
            unknown.AddComponent<BoxCollider>();
            Physics.SyncTransforms();

            ScreenTouchMapper.Init(new Dictionary<int, int> { [mappedCollider.GetInstanceID()] = MappedSensor });
            ScreenTouchMapper.Init(new Dictionary<int, int>());

            var cameraObject = _objects.Create("TouchCamera");
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 5.4f;
            _camera.targetTexture = _objects.Track(new RenderTexture(1080, 1080, 0));
            _camera.enabled = false;

            WriteSensorBlock(WrittenSensor, -2f, 1.5f, -1f, 2.5f);
        }

        /// <summary>Disposes the mapper and restores the static configuration.</summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_camera != null)
            {
                _camera.targetTexture = null;
            }
            ScreenTouchMapper.Dispose();
            ScreenTouchMapper.Dispose();
            ScreenTouchMapper.Override_TouchSimulationRadius = _saved.Radius;
            ScreenTouchMapper.UseOuterTouchAsSensor = _saved.OuterAsSensor;
            ScreenTouchMapper.UseGameplayTouchEnhancementFeatures = _saved.Enhancement;
            ScreenTouchMapper.TouchButtonRingEdge = _saved.Edge;
            ScreenTouchMapper.SubScreenEdge = _saved.SubScreen;
            _settings.Dispose();
            _objects.Dispose();
        }

        /// <summary>Resets the per-test switches.</summary>
        [SetUp]
        public void SetUp()
        {
            ScreenTouchMapper.UseOuterTouchAsSensor = false;
            ScreenTouchMapper.UseGameplayTouchEnhancementFeatures = false;
            ScreenTouchMapper.SubScreenEdge = Vector4.zero;
        }

        /// <summary>Checks that initialisation allocates the sample grid and the unit circle once.</summary>
        [Test]
        public void Init_AllocatesSamplesAndUnitCircle()
        {
            Assert.That(ScreenTouchMapper.IsInitialized, Is.True);
            Assert.That(ScreenTouchMapper.TouchPanelPositionSamples.Length, Is.EqualTo(1280 * 1280));
            Assert.That(ScreenTouchMapper.UnitCircle.Length, Is.EqualTo(ScreenTouchMapper.TOUCH_ANGLE_SMAPLE_COUNT));
            var first = ScreenTouchMapper.UnitCircle[0];
            Assert.That(first.x, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(first.y, Is.EqualTo(1f).Within(1e-5f));
        }

        /// <summary>Checks that the collider raycasts recorded the mapped sensor at the collider position only.</summary>
        [Test]
        public void Init_RecordsMappedColliders()
        {
            var samples = ScreenTouchMapper.TouchPanelPositionSamples;

            Assert.That(samples[Index(1f, 1f)] & SensorBit(MappedSensor), Is.EqualTo(SensorBit(MappedSensor)));
            Assert.That(samples[Index(-1f, 1f)] & SensorBit(MappedSensor), Is.EqualTo(0UL));
            Assert.That(samples[Index(-3f, -3f)], Is.EqualTo(0UL), "unknown colliders are not recorded");
        }

        /// <summary>Checks that a touch inside the play area reports the sensor under it.</summary>
        [Test]
        public void PositionToSensorState_PlayArea_ReportsSensors()
        {
            var result = Map(ScreenPoint(-1.5f, 2f));

            Assert.That(result.Sensors[WrittenSensor], Is.True);
            Assert.That(result.Buttons, Is.All.False);
            Assert.That(result.Raw & SensorBit(WrittenSensor), Is.EqualTo(SensorBit(WrittenSensor)));
            Assert.That(result.SensorOnly, Is.False);
        }

        /// <summary>Checks that a touch beyond the ring edge presses the outer button at its angle.</summary>
        [Test]
        public void PositionToSensorState_OuterRing_PressesButtons()
        {
            var result = Map(ScreenPoint(2.6f, 5.35f));

            Assert.That(result.Buttons[0], Is.True);
            Assert.That(result.Raw & 1UL, Is.EqualTo(1UL));
            Assert.That(result.Sensors, Is.All.False);
        }

        /// <summary>Checks that the outer ring maps to the outer sensors when it is used as a sensor ring.</summary>
        [Test]
        public void PositionToSensorState_OuterRingAsSensor_ReportsOuterSensors()
        {
            ScreenTouchMapper.UseOuterTouchAsSensor = true;

            var diagonal = Map(ScreenPoint(5.3f, 5.3f));
            var button = Map(ScreenPoint(2.6f, 5.35f));

            Assert.That(diagonal.Sensors[(int)SensorArea.D2 + 1], Is.True, "D2 uses the slot after C1/C2");
            Assert.That(diagonal.Raw & (1UL << 63), Is.EqualTo(1UL << 63));
            Assert.That(button.Sensors[0], Is.True, "the A1 button area is reported as sensor A1");
            Assert.That(button.Buttons[0], Is.False);
        }

        /// <summary>Checks that the secondary screen rectangle presses the P1 select button.</summary>
        [Test]
        public void PositionToSensorState_SubScreen_PressesSelect()
        {
            ScreenTouchMapper.SubScreenEdge = new Vector4(-1f, -3f, 1f, -5f);

            var result = Map(ScreenPoint(0f, -4f));

            Assert.That(result.Buttons[9], Is.True);
            Assert.That(result.Raw & (1UL << 9), Is.EqualTo(1UL << 9));
        }

        /// <summary>Checks that the gameplay enhancement turns a sensor touch into a sensor-only pointer.</summary>
        [Test]
        public void PositionToSensorState_Enhancement_MarksSensorOnly()
        {
            ScreenTouchMapper.UseGameplayTouchEnhancementFeatures = true;

            var result = Map(ScreenPoint(-1.5f, 2f));

            Assert.That(result.SensorOnly, Is.True);
            Assert.That(result.Sensors[WrittenSensor], Is.True);
            Assert.That(result.Raw & (1UL << 63), Is.EqualTo(1UL << 63));
        }

        /// <summary>Checks that negative screen positions and a missing camera are ignored.</summary>
        [Test]
        public void PositionToSensorState_InvalidInput_IsIgnored()
        {
            var sensors = new bool[35];
            var buttons = new bool[12];
            var raw = 123UL;
            var sensorOnly = false;

            ScreenTouchMapper.PositionToSensorState(sensors, buttons, _camera, new Vector3(-5f, 10f, 0f), 0f, ref raw, ref sensorOnly);
            ScreenTouchMapper.PositionToSensorState(sensors, buttons, null!, ScreenPoint(-1.5f, 2f), 0f, ref raw, ref sensorOnly);

            Assert.That(raw, Is.EqualTo(123UL));
            Assert.That(sensors, Is.All.False);
        }

        /// <summary>Checks that the geometry version changes only when a tuning value changes.</summary>
        [Test]
        public void OnPreUpdate_BumpsVersionOnlyOnChanges()
        {
            ScreenTouchMapper.OnPreUpdate();
            var before = PrivateMember.GetStaticField<ushort>(typeof(ScreenTouchMapper), "_version");

            ScreenTouchMapper.OnPreUpdate();
            var unchanged = PrivateMember.GetStaticField<ushort>(typeof(ScreenTouchMapper), "_version");
            _settings.Settings.Display.MainScreenTransform = true;
            _settings.Settings.Display.MainScreenOffset = 0.8f;
            ScreenTouchMapper.OnPreUpdate();
            var changed = PrivateMember.GetStaticField<ushort>(typeof(ScreenTouchMapper), "_version");
            _settings.Settings.Display.MainScreenTransform = false;
            _settings.Settings.Display.MainScreenOffset = 0.5f;
            ScreenTouchMapper.OnPreUpdate();

            Assert.That(unchanged, Is.EqualTo(before));
            Assert.That(changed, Is.EqualTo((ushort)(before + 1)));
            Assert.That(PrivateMember.GetStaticField<float>(typeof(ScreenTouchMapper), "_lastMainScreenOffset"), Is.EqualTo(1f));
            Assert.That(ScreenTouchMapper.FingerRadius, Is.EqualTo(_settings.Settings.Debug.TouchSimulationRadius));
        }

        /// <summary>Checks that a pointer is clicked once, held, and removed after it disappears.</summary>
        [Test]
        public void PointerState_ClicksOnceAndForgetsMissingPointers()
        {
            var state = new PointerInputState();

            state.BeginFrame();
            state.AddPointer(7, ScreenPoint(-1.5f, 2f), 0f, false, _camera);
            state.EndFrame();
            Assert.That(state.SensorClickedCount[WrittenSensor], Is.EqualTo(1));
            Assert.That(state.IsSensorCurrentlyOn(WrittenSensor), Is.True);

            state.BeginFrame();
            state.AddPointer(7, ScreenPoint(-1.5f, 2f), 0f, false, _camera);
            state.EndFrame();
            Assert.That(state.SensorClickedCount[WrittenSensor], Is.EqualTo(0), "a held pointer does not click again");

            state.BeginFrame();
            state.EndFrame();
            Assert.That(state.IsSensorCurrentlyOn(WrittenSensor), Is.False);

            state.BeginFrame();
            state.AddPointer(7, ScreenPoint(-1.5f, 2f), 0f, true, _camera);
            state.EndFrame();
            Assert.That(state.SensorClickedCount[WrittenSensor], Is.EqualTo(1), "the forgotten pointer clicks again");
            var states = new bool[35];
            state.ReadTouchPanel(states, new bool[35], new bool[35]);
            Assert.That(states[WrittenSensor], Is.True);
        }

        /// <summary>Checks that outer button pointers count button clicks, or sensor clicks when the ring acts as sensors.</summary>
        [Test]
        public void PointerState_OuterButtons_CountClicks()
        {
            var state = new PointerInputState();
            state.BeginFrame();
            state.AddPointer(1, ScreenPoint(2.6f, 5.35f), 0f, true, _camera);
            state.EndFrame();
            var buttons = new bool[12];
            state.ReadButtons(buttons, new bool[12], new bool[12]);
            Assert.That(state.ButtonClickedCount[0], Is.EqualTo(1));
            Assert.That(buttons[0], Is.True);
            Assert.That(state.IsButtonCurrentlyOn(0), Is.True);

            ScreenTouchMapper.UseOuterTouchAsSensor = true;
            var sensorState = new PointerInputState();
            sensorState.BeginFrame();
            sensorState.AddPointer(2, ScreenPoint(2.6f, 5.35f), 0f, true, _camera);
            sensorState.EndFrame();
            Assert.That(sensorState.SensorClickedCount[0], Is.EqualTo(1));
            Assert.That(sensorState.ButtonClickedCount[0], Is.EqualTo(0));
        }

        /// <summary>Converts a world position on the touch plane to a screen position of the test camera.</summary>
        /// <param name="x">The world x coordinate.</param>
        /// <param name="y">The world y coordinate.</param>
        /// <returns>The screen position.</returns>
        private static Vector3 ScreenPoint(float x, float y)
        {
            return new Vector3(540f + (x * 100f), 540f + (y * 100f), 0f);
        }

        /// <summary>Maps one screen position with no touch radius.</summary>
        /// <param name="position">The screen position.</param>
        /// <returns>The sensor and button states, the raw position data and the sensor-only flag.</returns>
        private (bool[] Sensors, bool[] Buttons, ulong Raw, bool SensorOnly) Map(Vector3 position)
        {
            var sensors = new bool[35];
            var buttons = new bool[12];
            var raw = 0UL;
            var sensorOnly = false;
            ScreenTouchMapper.PositionToSensorState(sensors, buttons, _camera, position, 0f, ref raw, ref sensorOnly);
            return (sensors, buttons, raw, sensorOnly);
        }

        /// <summary>Gets the position data bit of a sensor.</summary>
        /// <param name="sensor">The sensor index.</param>
        /// <returns>The bit mask.</returns>
        private static ulong SensorBit(int sensor)
        {
            return 1UL << (sensor + 12);
        }

        /// <summary>Gets the sample index of a world position.</summary>
        /// <param name="x">The world x coordinate.</param>
        /// <param name="y">The world y coordinate.</param>
        /// <returns>The index into the position samples.</returns>
        private static int Index(float x, float y)
        {
            return (((int)(x * 100) + 540) * 1280) + (int)(y * 100) + 540;
        }

        /// <summary>Marks a rectangle of the position samples with a sensor bit.</summary>
        /// <param name="sensor">The sensor index.</param>
        /// <param name="left">The left world coordinate.</param>
        /// <param name="bottom">The bottom world coordinate.</param>
        /// <param name="right">The right world coordinate.</param>
        /// <param name="top">The top world coordinate.</param>
        private static void WriteSensorBlock(int sensor, float left, float bottom, float right, float top)
        {
            var samples = ScreenTouchMapper.TouchPanelPositionSamples;
            var writable = MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(samples), samples.Length);
            for (var x = (int)(left * 100); x <= (int)(right * 100); x++)
            {
                for (var y = (int)(bottom * 100); y <= (int)(top * 100); y++)
                {
                    writable[((x + 540) * 1280) + y + 540] |= SensorBit(sensor);
                }
            }
        }
    }
}
