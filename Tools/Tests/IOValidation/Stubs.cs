using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

// Only external services/transports are replaced. Parsers, snapshots, and encoders
// are linked directly from the game sources in IOValidation.csproj.
namespace UnityEngine
{
    public struct Color
    {
        public float r, g, b, a;
        public Color(float red, float green, float blue, float alpha = 1)
        {
            r = red; g = green; b = blue; a = alpha;
        }
        public static bool operator ==(Color left, Color right) =>
            left.r == right.r && left.g == right.g && left.b == right.b && left.a == right.a;
        public static bool operator !=(Color left, Color right) => !(left == right);
        public override bool Equals(object? other) => other is Color color && this == color;
        public override int GetHashCode() => HashCode.Combine(r, g, b, a);
    }
    public static class Mathf
    {
        public static float Clamp01(float value) => Math.Clamp(value, 0, 1);
    }
}

namespace HidSharp
{
    public class HidStream
    {
        public int ReadTimeout { get; set; }
        public readonly List<byte[]> Writes = new();
        int _writeCount;
        public int WriteCount => Volatile.Read(ref _writeCount);
        public void Write(ReadOnlySpan<byte> data)
        {
            lock (Writes) Writes.Add(data.ToArray());
            Interlocked.Increment(ref _writeCount);
        }
    }
    public sealed class SerialStream : HidStream
    {
        public void Write(string data) { }
    }
    public sealed class HidDevice
    {
        public int OutputReportLength { get; set; } = 64;
        public byte ReportId { get; set; } = 7;
        public int GetMaxOutputReportLength() => OutputReportLength;
        public Descriptor GetReportDescriptor() => new() { OutputReports = new[] { new Report { ReportID = ReportId } } };
    }
    public sealed class Descriptor { public Report[] OutputReports { get; set; } = Array.Empty<Report>(); }
    public sealed class Report { public byte ReportID { get; set; } }
}

namespace UnityEngine.InputSystem.Controls
{
    public sealed class ButtonControl { public bool isPressed { get; set; } }
}

namespace UnityEngine.InputSystem
{
    public enum Key { None, W, E, D, C, X, Z, A, Q, Numpad9, NumpadMultiply, Numpad7, Numpad3 }

    public sealed class Keyboard
    {
        readonly Controls.ButtonControl[] _keys = new Controls.ButtonControl[13];
        public static Keyboard? current { get; set; }
        public Keyboard()
        {
            for (var i = 0; i < _keys.Length; i++) _keys[i] = new Controls.ButtonControl();
        }
        public Controls.ButtonControl this[Key key] => _keys[(int)key];
    }
}

namespace UnityEngine.InputSystem.LowLevel
{
    public enum GamepadButton
    {
        North, East, South, West, DpadUp, DpadRight, DpadDown, DpadLeft,
        LeftShoulder, RightShoulder, LeftStick, RightStick,
    }
}

namespace MajdataPlay.Diagnostics
{
    public static class MajDebug
    {
        public static void LogInfo(string tag, string message) { }
        public static void LogInfo(string message) { }
        public static void LogDebug(string tag, string message) { }
        public static void LogWarning(string tag, string message) { }
        public static void LogError(string tag, string message) { }
        public static void LogError(string tag, Exception exception) { }
        public static void LogException(Exception exception) { }
    }
}

namespace MajdataPlay.Settings
{
    public enum DeviceManufacturerOption { General, Yuan, Dao, Nov, Pipe }
    public enum ButtonRingDeviceOption { Keyboard, HID }
}

namespace MajdataPlay.Threading
{
    internal static class TaskExtensions
    {
        public static void RegisterAsWorker(this Task task, string name) { }
    }
}

namespace MajdataPlay
{
    internal static class MajEnv
    {
        public static SettingsStub Settings { get; } = new();
        public static CancellationToken GlobalCT { get; set; } = CancellationToken.None;
        public const int IO_DEVICE_RECONNECT_INTERVAL_MSEC = 20;
        public const ThreadPriority THREAD_PRIORITY_IO = ThreadPriority.Normal;
    }
    internal sealed class SettingsStub { public IOSettings IO { get; } = new(); }
    internal sealed class IOSettings
    {
        public InputSettings InputDevice { get; } = new();
        public OutputSettings OutputDevice { get; } = new();
    }
    internal sealed class InputSettings
    {
        public InputOptions ButtonRing { get; } = new();
        public InputOptions TouchPanel { get; } = new();
    }
    internal sealed class InputOptions
    {
        public bool Enable { get; set; } = true;
        public int PollingRateMs { get; set; }
        public SensitivityOptions Sensitivities { get; } = new();
    }
    internal sealed class SensitivityOptions
    {
        public short A { get; set; }
        public short B { get; set; }
        public short C { get; set; }
        public short D { get; set; }
        public short E { get; set; }
    }
    internal sealed class OutputSettings { public LedOptions Led { get; } = new(); }
    internal sealed class LedOptions
    {
        public bool Enable { get; set; } = true;
        public float Brightness { get; set; } = 1;
        public bool Throttler { get; set; } = true;
        public int RefreshRateMs { get; set; }
    }
}

namespace MajdataPlay.IO
{
    public static class IODetector
    {
        public readonly struct HidConnInfo
        {
            public int ProductId { get; init; }
            public int VendorId { get; init; }
            public string? DeviceName { get; init; }
            public bool Exclusice { get; init; }
            public int OpenPriority { get; init; }
        }
        public readonly struct SerialPortConnInfo { }
        public static int PlayerIndex { get; set; } = 1;
        public static MajdataPlay.Settings.DeviceManufacturerOption DeviceManufacturer { get; set; }
        public static MajdataPlay.Settings.ButtonRingDeviceOption ButtonRingDevice { get; set; }
        public static HidConnInfo ButtonRingHidConnInfo { get; set; }
        public static HidConnInfo LedDeviceHidConnInfo { get; set; }
        public static SerialPortConnInfo TouchPanelSerialConnInfo => default;
        public static SerialPortConnInfo LedDeviceSerialConnInfo => default;
    }
    internal static class DeviceTestExtensions
    {
        delegate void ParseReport(ReadOnlySpan<byte> data);
        static T Method<T>(IODevice device, string name) where T : Delegate =>
            device.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<T>(device);
        public static void Feed(this IODevice device, ReadOnlySpan<byte> data) => Method<ParseReport>(device, "Parse")(data);
        public static void Tick(this IODevice device) => Method<Action<CancellationToken>>(device, "Update")(CancellationToken.None);
        public static void DisconnectForTest(this IODevice device) => Method<Action>(device, "OnDisconnected")();
    }
    internal abstract class HidDevice : IODevice
    {
        static int _createdCount;
        int _connectCount;
        int _readCount;
        public static int CreatedCount => Volatile.Read(ref _createdCount);
        public int ConnectCount => Volatile.Read(ref _connectCount);
        public int ReadCount => Volatile.Read(ref _readCount);
        public IODetector.HidConnInfo Connection { get; }
        public HidSharp.HidStream Transport { get; } = new();
        protected HidSharp.HidStream? Stream => Transport;
        protected HidSharp.HidDevice? Device { get; } = new();
        protected HidDevice(IODetector.HidConnInfo connection)
        {
            Connection = connection;
            Interlocked.Increment(ref _createdCount);
        }
        protected override bool Connect(CancellationToken token)
        {
            Interlocked.Increment(ref _connectCount);
            OnConnected(token);
            return true;
        }
        protected override void Update(CancellationToken token) => Interlocked.Increment(ref _readCount);
        protected virtual void OnConnected(CancellationToken token) { }
    }
    internal abstract class SerialDevice : IODevice
    {
        public HidSharp.SerialStream Transport { get; } = new();
        protected HidSharp.SerialStream? Stream => Transport;
        protected SerialDevice(IODetector.SerialPortConnInfo connection) { }
        protected override bool Connect(CancellationToken token) => true;
        protected override void Update(CancellationToken token) { }
        protected virtual void OnConnected(CancellationToken token) { }
        public void ReconnectForTest() => OnConnected(CancellationToken.None);
    }
}
