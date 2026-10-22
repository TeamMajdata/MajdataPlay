using System;
using System.Collections.Generic;
using MajdataPlay.IO;
using MajdataPlay.Settings;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>
    /// Verifies the device selection of <see cref="GameDeviceManager.Init"/> for configurations that open no hardware:
    /// disabled capabilities, the keyboard button ring and the NPro WinUSB device, which only enumerates interfaces.
    /// </summary>
    public class GameDeviceManagerInitTests
    {
        /// <summary>The registered devices before the test.</summary>
        private IGameDevice[] _savedDevices = Array.Empty<IGameDevice>();

        /// <summary>The manufacturer before the test.</summary>
        private DeviceManufacturerOption _savedManufacturer;

        /// <summary>The installed default settings.</summary>
        private SettingsScope _settings = null!;

        /// <summary>Saves the registry and installs settings with every capability disabled.</summary>
        [SetUp]
        public void SetUp()
        {
            _savedDevices = Devices.ToArray();
            _savedManufacturer = IODetector.DeviceManufacturer;
            _settings = new SettingsScope();
            var input = _settings.Settings.IO.InputDevice;
            input.ButtonRing = new ButtonRingOptions { Enable = false };
            input.TouchPanel = new TouchPanelOptions { Enable = false };
            _settings.Settings.IO.OutputDevice.Led = new LedOptions { Enable = false };
            Reset();
        }

        /// <summary>Disposes created devices and restores the registry, manufacturer and settings.</summary>
        [TearDown]
        public void TearDown()
        {
            Reset();
            Devices.AddRange(_savedDevices);
            PrivateMember.SetStaticProperty(typeof(IODetector), nameof(IODetector.DeviceManufacturer), _savedManufacturer);
            _settings.Dispose();
        }

        /// <summary>Gets the registry list of the device manager.</summary>
        private static List<IGameDevice> Devices => PrivateMember.GetStaticField<List<IGameDevice>>(typeof(GameDeviceManager), "_devices");

        /// <summary>Checks that disabled capabilities register only the pointer devices for every manufacturer.</summary>
        /// <param name="manufacturer">The detected manufacturer.</param>
        [TestCase(DeviceManufacturerOption.General)]
        [TestCase(DeviceManufacturerOption.Yuan)]
        [TestCase(DeviceManufacturerOption.Dao)]
        [TestCase(DeviceManufacturerOption.Nov)]
        [TestCase(DeviceManufacturerOption.NPro)]
        [TestCase(DeviceManufacturerOption.Pipe)]
        public void Init_DisabledCapabilities_RegisterPointerDevicesOnly(DeviceManufacturerOption manufacturer)
        {
            PrivateMember.SetStaticProperty(typeof(IODetector), nameof(IODetector.DeviceManufacturer), manufacturer);

            GameDeviceManager.Init();
            GameDeviceManager.Init();

            Assert.That(GameDeviceManager.ButtonRing, Is.Null);
            Assert.That(GameDeviceManager.TouchPanel, Is.Null);
            Assert.That(GameDeviceManager.LedDevice, Is.Null);
            Assert.That(Devices, Is.EquivalentTo(new IGameDevice[] { GameDeviceManager.ScreenTouchPanel, GameDeviceManager.MouseTouchPanel }));
        }

        /// <summary>Checks that the general manufacturer with a keyboard ring starts the keyboard sampler.</summary>
        [Test]
        public void Init_KeyboardButtonRing_StartsTheKeyboardDevice()
        {
            PrivateMember.SetStaticProperty(typeof(IODetector), nameof(IODetector.DeviceManufacturer), DeviceManufacturerOption.General);
            _settings.Settings.IO.InputDevice.ButtonRing = new ButtonRingOptions { Enable = true };

            GameDeviceManager.Init();

            Assert.That(GameDeviceManager.ButtonRing, Is.TypeOf<KeyboardButtonRingDevice>());
            Assert.That(Devices, Does.Contain(GameDeviceManager.ButtonRing));
            Assert.That(PrivateMember.GetField<System.Threading.Thread?>(GameDeviceManager.ButtonRing!, "_daemon"), Is.Not.Null);
        }

        /// <summary>Checks that the NPro composite device serves every enabled capability from one instance.</summary>
        [Test]
        public void Init_NPro_SharesOneDevice()
        {
            PrivateMember.SetStaticProperty(typeof(IODetector), nameof(IODetector.DeviceManufacturer), DeviceManufacturerOption.NPro);
            _settings.Settings.IO.InputDevice.ButtonRing = new ButtonRingOptions { Enable = true };
            _settings.Settings.IO.InputDevice.TouchPanel = new TouchPanelOptions { Enable = true };
            _settings.Settings.IO.OutputDevice.Led = new LedOptions { Enable = true };

            GameDeviceManager.Init();

            Assert.That(GameDeviceManager.ButtonRing, Is.TypeOf<NproDevice>());
            Assert.That(GameDeviceManager.TouchPanel, Is.SameAs(GameDeviceManager.ButtonRing));
            Assert.That(GameDeviceManager.LedDevice, Is.SameAs(GameDeviceManager.ButtonRing));
            Assert.That(Devices, Has.Count.EqualTo(3));
        }

        /// <summary>Disposes started devices and clears the registry state.</summary>
        private static void Reset()
        {
            foreach (var device in Devices)
            {
                if (device is IODevice io)
                {
                    io.Dispose();
                }
            }
            Devices.Clear();
            PrivateMember.SetStaticField(typeof(GameDeviceManager), "_initialized", false);
            PrivateMember.SetStaticProperty(typeof(GameDeviceManager), nameof(GameDeviceManager.ButtonRing), null);
            PrivateMember.SetStaticProperty(typeof(GameDeviceManager), nameof(GameDeviceManager.TouchPanel), null);
            PrivateMember.SetStaticProperty(typeof(GameDeviceManager), nameof(GameDeviceManager.LedDevice), null);
        }
    }
}
