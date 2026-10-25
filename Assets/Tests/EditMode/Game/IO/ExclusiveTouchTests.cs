#if UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using MajdataPlay.IO;
using MajdataPlay.Settings;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>
    /// Verifies the exclusive capacitive touch pipeline: polygon mapping, finger tracking and the PDX and FLTouch report
    /// formats, without opening any USB device.
    /// </summary>
    public class ExclusiveTouchTests
    {
        /// <summary>The sensor slot of C1.</summary>
        private const int C1 = 16;

        /// <summary>The sensor slot of E1.</summary>
        private const int E1 = 26;

        /// <summary>The sensor slot of B1.</summary>
        private const int B1 = 8;

        /// <summary>A raw device coordinate pair that maps to the centre of E1.</summary>
        private static readonly (ushort X, ushort Y) s_rawE1 = (14490, 16384);

        /// <summary>A raw device coordinate pair that maps to the centre of C1.</summary>
        private static readonly (ushort X, ushort Y) s_rawC1 = (9178, 17749);

        /// <summary>The installed default settings.</summary>
        private SettingsScope _settings = null!;

        /// <summary>Installs default settings.</summary>
        [SetUp]
        public void SetUp()
        {
            _settings = new SettingsScope();
        }

        /// <summary>Restores the settings.</summary>
        [TearDown]
        public void TearDown()
        {
            _settings.Dispose();
        }

        /// <summary>Checks that polygon centres map to exactly their sensor.</summary>
        [Test]
        public void Mapper_PolygonCentres_MapToTheirSensor()
        {
            var mapper = new TouchSensorMapper(0, 0, 1440, 1440, 0, false);

            Assert.That(mapper.ParseTouchPoint(720, 308), Is.EqualTo(1UL << E1));
            Assert.That(mapper.ParseTouchPoint(780, 723), Is.EqualTo(1UL << C1));
        }

        /// <summary>Checks that points outside the canvas map to nothing.</summary>
        [Test]
        public void Mapper_OutsideCanvas_MapsToNothing()
        {
            var mapper = new TouchSensorMapper(0, 0, 1440, 1440, 40, false);

            Assert.That(mapper.ParseTouchPoint(-10, 700), Is.EqualTo(0UL));
            Assert.That(mapper.ParseTouchPoint(700, 1500), Is.EqualTo(0UL));
        }

        /// <summary>Checks that the flip option swaps the axes and the coordinate range is mapped linearly.</summary>
        [Test]
        public void Mapper_FlipAndRange_TransformTheCoordinates()
        {
            var flipped = new TouchSensorMapper(0, 0, 1440, 1440, 0, true);
            var scaled = new TouchSensorMapper(0, 0, 2880, 720, 0, false);

            Assert.That(flipped.ParseTouchPoint(308, 720), Is.EqualTo(1UL << E1));
            Assert.That(scaled.ParseTouchPoint(1440, 154), Is.EqualTo(1UL << E1));
        }

        /// <summary>Checks that a touch radius and per-group extra radii widen the hit area.</summary>
        [Test]
        public void Mapper_RadiusAndExtraRadius_WidenTheHitArea()
        {
            var point = (X: 760f, Y: 430f);
            var exact = new TouchSensorMapper(0, 0, 1440, 1440, 0, false);
            var widenedE = new TouchSensorMapper(0, 0, 1440, 1440, 0, false, eExtraRadius: 40);
            var widenedAll = new TouchSensorMapper(0, 0, 1440, 1440, 40, false);
            var shrunk = new TouchSensorMapper(0, 0, 1440, 1440, 10, false, 0, -50, 0, 0, 0);

            Assert.That(exact.ParseTouchPoint(point.X, point.Y), Is.EqualTo(1UL << B1));
            Assert.That(widenedE.ParseTouchPoint(point.X, point.Y), Is.EqualTo((1UL << B1) | (1UL << E1)));
            Assert.That(widenedAll.ParseTouchPoint(point.X, point.Y) & ((1UL << B1) | (1UL << E1)), Is.EqualTo((1UL << B1) | (1UL << E1)));
            Assert.That(shrunk.ParseTouchPoint(point.X, point.Y) & (1UL << B1), Is.EqualTo(1UL << B1), "a negative extra radius falls back to point tests");
        }

        /// <summary>Checks that several fingers are combined and a release removes only that finger.</summary>
        [Test]
        public void State_Fingers_AreCombinedAndReleasedIndividually()
        {
            var state = CreateIdentityState();

            state.HandleFinger(720, 308, 3, true);
            state.HandleFinger(780, 723, 9, true);
            Assert.That(state.IsSensorCurrentlyOn(E1) && state.IsSensorCurrentlyOn(C1), Is.True);

            state.HandleFinger(0, 0, 3, false);
            Assert.That(state.IsSensorCurrentlyOn(E1), Is.False);
            Assert.That(state.IsSensorCurrentlyOn(C1), Is.True);
        }

        /// <summary>Checks that out of range finger ids are ignored.</summary>
        [Test]
        public void State_InvalidFingerIds_AreIgnored()
        {
            var state = CreateIdentityState();

            state.HandleFinger(720, 308, -1, true);
            state.HandleFinger(720, 308, 256, true);
            state.HandleFrame(new List<TouchPanelState.TouchUpdate> { new TouchPanelState.TouchUpdate(720, 308, 300, true) });

            Assert.That(state.IsSensorCurrentlyOn(E1), Is.False);
        }

        /// <summary>Checks that a frame replaces the previous fingers while a release batch keeps them.</summary>
        [Test]
        public void State_FrameReplacesAndReleasesKeep()
        {
            var state = CreateIdentityState();
            state.HandleFinger(720, 308, 1, true);

            state.HandleReleases(new List<TouchPanelState.TouchUpdate> { new TouchPanelState.TouchUpdate(0, 0, 2, false) });
            Assert.That(state.IsSensorCurrentlyOn(E1), Is.True, "release batches keep other fingers");

            state.HandleFrame(new List<TouchPanelState.TouchUpdate> { new TouchPanelState.TouchUpdate(780, 723, 2, true) });
            Assert.That(state.IsSensorCurrentlyOn(E1), Is.False, "a frame snapshot releases missing fingers");
            Assert.That(state.IsSensorCurrentlyOn(C1), Is.True);
        }

        /// <summary>Checks that fingers expire after the timeout and that clearing releases every finger.</summary>
        [Test]
        public void State_TimeoutAndClear()
        {
            var expiring = new TouchPanelState(0, 0, 1440, 1440, false, 0, timeoutMilliseconds: 0);
            expiring.HandleFinger(720, 308, 1, true);
            var start = Stopwatch.GetTimestamp();
            while (Stopwatch.GetTimestamp() == start)
            {
            }

            expiring.OnPreUpdate();
            var hadOn = new bool[35];
            var states = new bool[35];
            expiring.ReadTouchPanel(states, hadOn, new bool[35]);
            Assert.That(states[E1], Is.False, "the finger timed out");
            Assert.That(hadOn[E1], Is.True, "the touch is still latched for the frame");

            var cleared = CreateIdentityState();
            cleared.HandleFinger(720, 308, 1, true);
            cleared.Clear();
            Assert.That(cleared.IsSensorCurrentlyOn(E1), Is.False);
        }

        /// <summary>Checks the new PDX report layout with pressed and released slots.</summary>
        [Test]
        public void Pdx_NewReport_TracksFingers()
        {
            var device = CreatePdx();
            var report = new byte[64];
            report[0] = 2;
            WriteNewSlot(report, 0, 0x01, 4, s_rawE1);
            WriteNewSlot(report, 1, 0x01, 5, s_rawC1);
            WriteNewSlot(report, 2, 0x00, 6, s_rawC1);

            device.ParseReport(report);
            Assert.That(device.IsSensorCurrentlyOn(E1) && device.IsSensorCurrentlyOn(C1), Is.True);

            var release = new byte[64];
            release[0] = 2;
            WriteNewSlot(release, 0, 0x02, 4, s_rawE1);
            device.ParseReport(release);
            Assert.That(device.IsSensorCurrentlyOn(E1), Is.False);
            Assert.That(device.IsSensorCurrentlyOn(C1), Is.True);

            device.OnPreUpdate();
            var hadOn = new bool[35];
            device.ReadTouchPanel(new bool[35], hadOn, new bool[35]);
            Assert.That(hadOn[E1], Is.True);
        }

        /// <summary>Checks that PDX reports with another report id or an insufficient length are ignored.</summary>
        [Test]
        public void Pdx_InvalidReports_AreIgnored()
        {
            var device = CreatePdx();
            var wrongId = new byte[64];
            wrongId[0] = 3;
            WriteNewSlot(wrongId, 0, 0x01, 1, s_rawE1);
            var tooShort = new byte[60];
            tooShort[0] = 2;
            WriteNewSlot(tooShort, 0, 0x01, 1, s_rawE1);

            device.ParseReport(wrongId);
            device.ParseReport(tooShort);

            Assert.That(device.IsSensorCurrentlyOn(E1), Is.False);
        }

        /// <summary>Checks that an old AT32 PDX frame split over two reports is applied when complete.</summary>
        [Test]
        public void Pdx_OldFrameSplitAcrossReports_IsAppliedWhenComplete()
        {
            var device = CreatePdx();
            SetPdxKind(device, 1);
            var first = new byte[64];
            first[0] = 2;
            first[61] = 7;
            for (var slot = 0; slot < 6; slot++)
            {
                WriteAreaSlot(first, 1, slot, (byte)slot, s_rawE1, 10);
            }
            var second = new byte[64];
            second[0] = 2;
            WriteAreaSlot(second, 1, 0, 6, s_rawC1, 10);

            device.ParseReport(first);
            Assert.That(device.IsSensorCurrentlyOn(E1), Is.False, "the frame is not complete yet");
            device.ParseReport(second);

            Assert.That(device.IsSensorCurrentlyOn(E1), Is.True);
            Assert.That(device.IsSensorCurrentlyOn(C1), Is.True);
            Assert.That(PrivateMember.GetProperty<string>(device, "DiagnosticName"), Is.EqualTo("PDX-AT32"));
        }

        /// <summary>Checks that old PDX releases apply immediately and headerless continuations are dropped.</summary>
        [Test]
        public void Pdx_OldReleasesAndHeaderlessReports()
        {
            var device = CreatePdx();
            SetPdxKind(device, 1);
            var orphan = new byte[64];
            orphan[0] = 2;
            WriteAreaSlot(orphan, 1, 0, 1, s_rawE1, 10);
            device.ParseReport(orphan);
            Assert.That(device.IsSensorCurrentlyOn(E1), Is.False, "no frame header was seen");

            var press = new byte[64];
            press[0] = 2;
            press[61] = 1;
            WriteAreaSlot(press, 1, 0, 1, s_rawE1, 10);
            device.ParseReport(press);
            Assert.That(device.IsSensorCurrentlyOn(E1), Is.True);

            var release = new byte[64];
            release[0] = 2;
            release[61] = 8;
            WriteAreaSlot(release, 1, 0, 1, s_rawE1, 0);
            device.ParseReport(release);
            Assert.That(device.IsSensorCurrentlyOn(E1), Is.False, "a release is applied before the frame completes");
        }

        /// <summary>Checks PDX endpoint selection by interface number and the disconnect reset.</summary>
        [Test]
        public void Pdx_ResolveEndpointAndReset()
        {
            var device = CreatePdx();

            PrivateMember.Invoke(device, "ResolveEndpoint", new WinUsbIo.DevicePath { InterfaceNumber = 0 });
            Assert.That(PrivateMember.GetProperty<string>(device, "DiagnosticName"), Is.EqualTo("PDX-AT32"));
            PrivateMember.Invoke(device, "ResolveEndpoint", new WinUsbIo.DevicePath { InterfaceNumber = 1 });
            Assert.That(PrivateMember.GetProperty<string>(device, "DiagnosticName"), Is.EqualTo("PDX"));
            var error = Assert.Throws<TargetInvocationException>(() =>
                PrivateMember.Invoke(device, "ResolveEndpoint", new WinUsbIo.DevicePath { InterfaceNumber = 2 }));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());

            var report = new byte[64];
            report[0] = 2;
            WriteNewSlot(report, 0, 0x01, 1, s_rawE1);
            device.ParseReport(report);
            PrivateMember.Invoke(device, "OnDeviceConnected");
            device.ResetTransport();
            Assert.That(device.IsSensorCurrentlyOn(E1), Is.False);
        }

        /// <summary>Checks the FLTouch report layout including split frames, releases and invalid reports.</summary>
        [Test]
        public void Fl_Reports_TrackFingers()
        {
            var device = new FlTouchDevice(string.Empty, 0, default);
            var first = new byte[64];
            first[0] = 2;
            first[1] = 7;
            for (var slot = 0; slot < 6; slot++)
            {
                WriteAreaSlot(first, 2, slot, (byte)slot, s_rawE1, 5);
            }
            var second = new byte[64];
            second[0] = 2;
            WriteAreaSlot(second, 2, 0, 6, s_rawC1, 5);

            device.ParseReport(new byte[61]);
            device.ParseReport(second);
            Assert.That(device.IsSensorCurrentlyOn(C1), Is.False, "headerless continuation is dropped");
            device.ParseReport(first);
            Assert.That(device.IsSensorCurrentlyOn(E1), Is.False);
            device.ParseReport(second);
            Assert.That(device.IsSensorCurrentlyOn(E1) && device.IsSensorCurrentlyOn(C1), Is.True);

            var release = new byte[64];
            release[0] = 2;
            release[1] = 1;
            WriteAreaSlot(release, 2, 0, 6, s_rawC1, 0);
            device.ParseReport(release);
            Assert.That(device.IsSensorCurrentlyOn(C1), Is.False);

            var wrongId = new byte[64];
            wrongId[0] = 1;
            wrongId[1] = 1;
            WriteAreaSlot(wrongId, 2, 0, 9, s_rawC1, 5);
            device.ParseReport(wrongId);
            Assert.That(device.IsSensorCurrentlyOn(C1), Is.False);
        }

        /// <summary>Checks the FLTouch endpoint, diagnostic name and reset behaviour.</summary>
        [Test]
        public void Fl_ConfigurationAndReset()
        {
            var device = new FlTouchDevice(string.Empty, 0, default);
            var report = new byte[64];
            report[0] = 2;
            report[1] = 1;
            WriteAreaSlot(report, 2, 0, 1, s_rawE1, 5);
            device.ParseReport(report);
            device.OnPreUpdate();

            PrivateMember.Invoke(device, "ResolveEndpoint", new WinUsbIo.DevicePath { InterfaceNumber = 0 });
            var error = Assert.Throws<TargetInvocationException>(() =>
                PrivateMember.Invoke(device, "ResolveEndpoint", new WinUsbIo.DevicePath { InterfaceNumber = 1 }));
            PrivateMember.Invoke(device, "OnDeviceConnected");
            device.ResetTransport();

            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(PrivateMember.GetProperty<string>(device, "DiagnosticName"), Is.EqualTo("FL"));
            Assert.That(PrivateMember.GetProperty<string>(device, "DaemonThreadName"), Is.EqualTo("IO/FL Thread"));
            Assert.That(device.IsSensorCurrentlyOn(E1), Is.False);
        }

        /// <summary>Checks that the exclusive host reports a released panel until a candidate is active and then forwards to it.</summary>
        [Test]
        public void Host_ForwardsToTheActiveCandidate()
        {
            var host = new ExclusiveTouchHost();
            host.OnPreUpdate();
            var states = new bool[35];
            var hadOn = new bool[35];
            var hadOff = new bool[35];
            host.ReadTouchPanel(states, hadOn, hadOff);
            Assert.That(states, Is.All.False);
            Assert.That(hadOff, Is.All.True);
            Assert.That(host.IsSensorCurrentlyOn(E1), Is.False);

            var candidates = PrivateMember.GetField<UsbDevice[]>(host, "_candidates");
            Assert.That(candidates[0], Is.TypeOf<PdxTouchDevice>());
            Assert.That(candidates[1], Is.TypeOf<FlTouchDevice>());
            PrivateMember.SetField(host, "_active", candidates[1]);
            var report = new byte[64];
            report[0] = 2;
            report[1] = 1;
            WriteAreaSlot(report, 2, 0, 1, s_rawE1, 5);
            DeviceTestSupport.BindParser(host)(report);
            Assert.That(host.IsSensorCurrentlyOn(E1), Is.True);
            host.OnPreUpdate();
            host.ReadTouchPanel(states, hadOn, hadOff);

            Assert.That(hadOn[E1], Is.True);

            PrivateMember.Invoke(host, "OnDisconnected");
            Assert.That(PrivateMember.GetField<UsbDevice?>(host, "_active"), Is.Null);
            Assert.That(candidates[1].IsSensorCurrentlyOnFor(E1), Is.False);
            Assert.That(PrivateMember.GetProperty<TimeSpan>(host, "PollingInterval"), Is.EqualTo(TimeSpan.Zero));
        }

        /// <summary>Creates a finger tracker with an identity coordinate mapping and no radius.</summary>
        /// <returns>The tracker.</returns>
        private static TouchPanelState CreateIdentityState()
        {
            return new TouchPanelState(0, 0, 1440, 1440, false, 0);
        }

        /// <summary>Creates a PDX device with no touch radius.</summary>
        /// <returns>The device.</returns>
        private static PdxTouchDevice CreatePdx()
        {
            return new PdxTouchDevice(0x3356, 0x3003, string.Empty, 0, default(CapacitiveTouchPanelRadiusOffsetConfig));
        }

        /// <summary>Selects the PDX report format.</summary>
        /// <param name="device">The device.</param>
        /// <param name="kind">0 for the new format and 1 for the old AT32 format.</param>
        private static void SetPdxKind(PdxTouchDevice device, int kind)
        {
            var field = typeof(PdxTouchDevice).GetField("_kind", BindingFlags.Instance | BindingFlags.NonPublic)!;
            field.SetValue(device, Enum.ToObject(field.FieldType, kind));
        }

        /// <summary>Writes one slot of the new PDX format.</summary>
        /// <param name="report">The report buffer.</param>
        /// <param name="slot">The slot index.</param>
        /// <param name="status">The status byte; bit 0 means pressed.</param>
        /// <param name="finger">The finger id.</param>
        /// <param name="position">The raw coordinates.</param>
        private static void WriteNewSlot(byte[] report, int slot, byte status, byte finger, (ushort X, ushort Y) position)
        {
            var index = 1 + (slot * 6);
            report[index] = status;
            report[index + 1] = finger;
            BitConverter.GetBytes(position.X).CopyTo(report, index + 2);
            BitConverter.GetBytes(position.Y).CopyTo(report, index + 4);
        }

        /// <summary>Writes one ten byte slot carrying a contact area, as used by old PDX and FLTouch reports.</summary>
        /// <param name="report">The report buffer.</param>
        /// <param name="start">The offset of the first slot.</param>
        /// <param name="slot">The slot index.</param>
        /// <param name="finger">The finger id.</param>
        /// <param name="position">The raw coordinates.</param>
        /// <param name="area">The contact width and height; zero means released.</param>
        private static void WriteAreaSlot(byte[] report, int start, int slot, byte finger, (ushort X, ushort Y) position, ushort area)
        {
            var index = start + (slot * 10);
            report[index] = 0x04;
            report[index + 1] = finger;
            BitConverter.GetBytes(position.X).CopyTo(report, index + 2);
            BitConverter.GetBytes(position.Y).CopyTo(report, index + 4);
            BitConverter.GetBytes(area).CopyTo(report, index + 6);
            BitConverter.GetBytes(area).CopyTo(report, index + 8);
        }
    }

    /// <summary>Provides a sensor query for the USB transport base type, whose touch interface is implemented by each device.</summary>
    internal static class UsbDeviceTouchExtensions
    {
        /// <summary>Reads the live state of a sensor when the device is a touch panel.</summary>
        /// <param name="device">The USB device.</param>
        /// <param name="index">The sensor slot.</param>
        /// <returns>True when the device is a touch panel reporting the sensor as pressed.</returns>
        public static bool IsSensorCurrentlyOnFor(this UsbDevice device, int index)
        {
            return device is ITouchPanelDevice touch && touch.IsSensorCurrentlyOn(index);
        }
    }
}
#endif
