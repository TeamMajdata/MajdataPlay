using System;
using System.Collections.Generic;
using MajdataPlay.IO;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>Verifies the LED output facade <see cref="OutputManager"/> and the device registry <see cref="GameDeviceManager"/>.</summary>
    public class OutputManagerTests
    {
        /// <summary>The nested LED facade type of the output manager.</summary>
        private static readonly Type s_ledDeviceType = typeof(OutputManager).GetNestedType("LedDevice", System.Reflection.BindingFlags.NonPublic)!;

        /// <summary>The LED device installed before the test.</summary>
        private object? _originalDevice;

        /// <summary>The cabinet brightness before the test.</summary>
        private byte _originalBrightness;

        /// <summary>The ring colours before the test.</summary>
        private Color[] _originalColors = Array.Empty<Color>();

        /// <summary>The scripted LED device.</summary>
        private FakeInputDevice _device = null!;

        /// <summary>Installs a scripted LED device.</summary>
        [SetUp]
        public void SetUp()
        {
            _originalDevice = PrivateMember.GetStaticField<object?>(s_ledDeviceType, "_device");
            _originalBrightness = PrivateMember.GetStaticField<byte>(typeof(OutputManager), "_cabinetLightBrightness");
            _originalColors = (Color[])PrivateMember.GetStaticField<Color[]>(typeof(OutputManager), "_ledRingColors").Clone();
            _device = new FakeInputDevice();
            PrivateMember.SetStaticField(s_ledDeviceType, "_device", _device);
        }

        /// <summary>Restores the LED facade.</summary>
        [TearDown]
        public void TearDown()
        {
            PrivateMember.SetStaticField(s_ledDeviceType, "_device", _originalDevice);
            PrivateMember.SetStaticField(typeof(OutputManager), "_cabinetLightBrightness", _originalBrightness);
            _originalColors.CopyTo(PrivateMember.GetStaticField<Color[]>(typeof(OutputManager), "_ledRingColors"), 0);
        }

        /// <summary>Checks that ring colours are forwarded with the current cabinet brightness.</summary>
        [Test]
        public void SetLedRingColorData_ForwardsColoursAndBrightness()
        {
            var colors = new Color[8];
            colors[3] = Color.magenta;

            OutputManager.SetCabinetLightBrightness(1f);
            OutputManager.SetLedRingColorData(colors);

            Assert.That(_device.LastColors, Is.EqualTo(colors));
            Assert.That(_device.LastCabinetBrightness, Is.EqualTo(255));
            Assert.That(_device.LedWrites, Is.EqualTo(2));
        }

        /// <summary>Checks that the cabinet brightness is clamped and rounded to a byte.</summary>
        /// <param name="brightness">The requested brightness.</param>
        /// <param name="expected">The expected byte value.</param>
        [TestCase(0.5f, (byte)128)]
        [TestCase(2f, (byte)255)]
        [TestCase(-1f, (byte)0)]
        [TestCase(0.25f, (byte)64)]
        public void SetCabinetLightBrightness_ClampsAndRounds(float brightness, byte expected)
        {
            OutputManager.SetCabinetLightBrightness(brightness);

            Assert.That(_device.LastCabinetBrightness, Is.EqualTo(expected));
        }

        /// <summary>Checks that the facade reports the device connection and tolerates a missing device.</summary>
        [Test]
        public void LedFacade_ReportsConnectionAndToleratesMissingDevice()
        {
            Assert.That(PrivateMember.GetStaticProperty<bool>(s_ledDeviceType, "IsConnected"), Is.False);
            _device.IsConnected = true;
            Assert.That(PrivateMember.GetStaticProperty<bool>(s_ledDeviceType, "IsConnected"), Is.True);

            PrivateMember.SetStaticField(s_ledDeviceType, "_device", null);
            Assert.DoesNotThrow(() => OutputManager.SetLedRingColorData(new Color[8]));
            Assert.That(PrivateMember.GetStaticProperty<bool>(s_ledDeviceType, "IsConnected"), Is.False);
        }

        /// <summary>Checks that initialisation adopts the registry's LED device and sends it the current colours.</summary>
        [Test]
        public void Init_AdoptsTheRegistryDevice()
        {
            var initialized = PrivateMember.GetStaticField<bool>(typeof(GameDeviceManager), "_initialized");
            var registered = GameDeviceManager.LedDevice;
            var adopted = new FakeInputDevice();
            try
            {
                PrivateMember.SetStaticField(typeof(GameDeviceManager), "_initialized", true);
                PrivateMember.SetStaticProperty(typeof(GameDeviceManager), nameof(GameDeviceManager.LedDevice), adopted);

                OutputManager.Init();

                Assert.That(adopted.LedWrites, Is.EqualTo(1));
                Assert.That(PrivateMember.GetStaticField<object?>(s_ledDeviceType, "_device"), Is.SameAs(adopted));
            }
            finally
            {
                PrivateMember.SetStaticField(typeof(GameDeviceManager), "_initialized", initialized);
                PrivateMember.SetStaticProperty(typeof(GameDeviceManager), nameof(GameDeviceManager.LedDevice), registered);
            }
        }

        /// <summary>Checks that registered devices receive one frame update each, even when registered twice.</summary>
        [Test]
        public void GameDeviceManager_RegistersDevicesOnce()
        {
            var devices = PrivateMember.GetStaticField<List<IGameDevice>>(typeof(GameDeviceManager), "_devices");
            var snapshot = devices.ToArray();
            var device = new FakeInputDevice();
            try
            {
                devices.Clear();
                PrivateMember.InvokeStatic(typeof(GameDeviceManager), "Register", device);
                PrivateMember.InvokeStatic(typeof(GameDeviceManager), "Register", device);
                PrivateMember.InvokeStatic(typeof(GameDeviceManager), "Register", new object?[] { null });

                GameDeviceManager.OnPreUpdate();

                Assert.That(devices, Has.Count.EqualTo(1));
                Assert.That(device.PreUpdates, Is.EqualTo(1));
            }
            finally
            {
                devices.Clear();
                devices.AddRange(snapshot);
            }
        }

        /// <summary>Checks that the always available pointer devices are singletons that publish released states without input.</summary>
        [Test]
        public void GameDeviceManager_PointerDevicesPublishReleasedStatesWithoutInput()
        {
            var screen = GameDeviceManager.ScreenTouchPanel;
            var mouse = GameDeviceManager.MouseTouchPanel;

            screen.OnPreUpdate();
            var states = new bool[35];
            var hadOn = new bool[35];
            screen.ReadTouchPanel(states, hadOn, new bool[35]);
            var buttons = new bool[12];
            screen.ReadButtons(buttons, new bool[12], new bool[12]);

            Assert.That(GameDeviceManager.ScreenTouchPanel, Is.SameAs(screen));
            Assert.That(GameDeviceManager.MouseTouchPanel, Is.SameAs(mouse));
            Assert.That(states, Is.All.False);
            Assert.That(buttons, Is.All.False);
            Assert.That(screen.IsSensorCurrentlyOn(0), Is.False);
            Assert.That(screen.IsButtonCurrentlyOn(0), Is.False);
            Assert.That(screen.ButtonClickedCount.Length, Is.EqualTo(8));
            Assert.That(screen.SensorClickedCount.Length, Is.EqualTo(34));
            Assert.That(screen.IsConnected, Is.EqualTo(UnityEngine.InputSystem.Touchscreen.current != null));

            mouse.OnPreUpdate();
            var mouseStates = new bool[35];
            mouse.ReadTouchPanel(mouseStates, new bool[35], new bool[35]);
            var mouseButtons = new bool[12];
            mouse.ReadButtons(mouseButtons, new bool[12], new bool[12]);
            Assert.That(mouseStates, Is.All.False);
            Assert.That(mouseButtons, Is.All.False);
            Assert.That(mouse.IsSensorCurrentlyOn(3), Is.False);
            Assert.That(mouse.IsButtonCurrentlyOn(3), Is.False);
            Assert.That(mouse.ButtonClickedCount.Length, Is.EqualTo(8));
            Assert.That(mouse.SensorClickedCount.Length, Is.EqualTo(34));
            Assert.That(mouse.IsConnected, Is.EqualTo(UnityEngine.InputSystem.Mouse.current != null));
        }
    }
}
