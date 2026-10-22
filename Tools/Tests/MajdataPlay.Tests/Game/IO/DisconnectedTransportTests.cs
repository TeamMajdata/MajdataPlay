using System;
using System.IO;
using System.Reflection;
using System.Threading;
using MajdataPlay.IO;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>
    /// Verifies how the HID, serial and WinUSB transports behave without a connection, which is the state the daemon sees
    /// after a device was unplugged; no device is opened.
    /// </summary>
    public class DisconnectedTransportTests
    {
        /// <summary>The installed default settings.</summary>
        private SettingsScope _settings = null!;

        /// <summary>The player slot before the test.</summary>
        private int _playerIndex;

        /// <summary>Installs default settings and selects player 1.</summary>
        [SetUp]
        public void SetUp()
        {
            _settings = new SettingsScope();
            _playerIndex = DeviceTestSupport.SetPlayerIndex(1);
        }

        /// <summary>Restores the settings and player slot.</summary>
        [TearDown]
        public void TearDown()
        {
            DeviceTestSupport.SetPlayerIndex(_playerIndex);
            _settings.Dispose();
        }

        /// <summary>Checks that a HID device without a stream reports the disconnection and disconnecting is idempotent.</summary>
        [Test]
        public void Hid_UpdateWithoutStream_Throws()
        {
            var device = new YuanButtonRingDevice();

            Assert.Throws<IOException>(() => DeviceTestSupport.BindTokenAction(device, "Update")(CancellationToken.None));
            Assert.DoesNotThrow(() => PrivateMember.Invoke(device, "Disconnect"));
            Assert.DoesNotThrow(() => PrivateMember.Invoke(device, "Disconnect"));
            Assert.DoesNotThrow(() => PrivateMember.Invoke(device, "OnConnected", CancellationToken.None));
        }

        /// <summary>Checks that a serial device without a stream reports the disconnection.</summary>
        [Test]
        public void Serial_UpdateWithoutStream_Throws()
        {
            var device = new RosenTouchPanelDevice();
            var led = new RosenLedDevice();

            Assert.Throws<IOException>(() => DeviceTestSupport.BindTokenAction(device, "Update")(CancellationToken.None));
            Assert.Throws<IOException>(() => DeviceTestSupport.BindTokenAction(led, "Update")(CancellationToken.None));
            Assert.DoesNotThrow(() => PrivateMember.Invoke(device, "Disconnect"));
        }

        /// <summary>Checks that a cancelled token stops the HID and serial updates before any I/O.</summary>
        [Test]
        public void CancelledUpdates_ThrowCancellation()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => DeviceTestSupport.BindTokenAction(new YuanButtonRingDevice(), "Update")(cancellation.Token));
            Assert.Throws<OperationCanceledException>(() => DeviceTestSupport.BindTokenAction(new RosenTouchPanelDevice(), "Update")(cancellation.Token));
            var connect = Assert.Throws<TargetInvocationException>(() => PrivateMember.Invoke<bool>(new YuanButtonRingDevice(), "Connect", cancellation.Token));
            Assert.That(connect.InnerException, Is.InstanceOf<OperationCanceledException>(), "connection checks cancellation first");
        }

        /// <summary>Checks that the NPro device fails to write LED frames and read input without a WinUSB connection.</summary>
        [Test]
        public void Npro_WithoutConnection_ReportsIOFailures()
        {
            var device = new NproDevice(true);
            device.WriteLeds(new Color[8], 255);

            var updateLeds = Assert.Throws<TargetInvocationException>(() => PrivateMember.Invoke(device, "UpdateLeds", CancellationToken.None));
            var update = Assert.Throws<IOException>(() => DeviceTestSupport.BindTokenAction(device, "Update")(CancellationToken.None));
            var readOnly = new NproDevice(false);

            Assert.That(updateLeds.InnerException, Is.TypeOf<IOException>());
            Assert.That(update, Is.Not.Null);
            Assert.Throws<IOException>(() => DeviceTestSupport.BindTokenAction(readOnly, "Update")(CancellationToken.None));
            Assert.Throws<IOException>(() => readOnly.UpdateTransport(CancellationToken.None));
            Assert.DoesNotThrow(() => readOnly.CloseTransport());
            Assert.That(readOnly.IsConnected, Is.False);
            Assert.That(device.ConnectTransport(CancellationToken.None), Is.False, "no NPro interface is present");
        }

        /// <summary>Checks that the exclusive touch host finds no candidate and fails updates of an unconnected candidate.</summary>
        [Test]
        public void ExclusiveHost_WithoutDevices()
        {
            var host = new ExclusiveTouchHost();

            Assert.That(PrivateMember.Invoke<bool>(host, "Connect", CancellationToken.None), Is.False);
            Assert.That(PrivateMember.GetField<UsbDevice?>(host, "_active"), Is.Null);

            var candidates = PrivateMember.GetField<UsbDevice[]>(host, "_candidates");
            PrivateMember.SetField(host, "_active", candidates[0]);
            Assert.Throws<IOException>(() => DeviceTestSupport.BindTokenAction(host, "Update")(CancellationToken.None));
            Assert.DoesNotThrow(() => PrivateMember.Invoke(host, "Disconnect"));
            Assert.That(PrivateMember.GetProperty<string>(host, "DaemonThreadName"), Is.EqualTo("IO/ExclusiveTouch Thread"));
        }
    }
}
