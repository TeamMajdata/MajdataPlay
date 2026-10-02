using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using MajdataPlay;
using MajdataPlay.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

internal static partial class Program
{
    static int _assertions;

    static void Main()
    {
        IODeviceLifecycleTests.Run();
        VerifyDeviceHierarchy();
        VerifyKeyboardMappings();
        VerifyKeyboardSampling();
        VerifyButtonProtocols();
        VerifyHidFunctionButtonOverlay();
        VerifySharedDaoCapabilities();
        VerifySharedDaoPolling();
        VerifySerialTouchFraming();
        VerifyPipeFraming();
        VerifySerialLedPackets();
        VerifyDaoLedPackets();
        VerifySharedDeviceFactory();
        VerifyPointerDevices();
        VerifySupplementalDeviceFactory();
        Console.WriteLine($"IO_VALIDATION_PASSED ({_assertions} assertions)");
    }

    static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    static void VerifyDeviceHierarchy()
    {
        var implementations = new (Type Device, Type Transport)[]
        {
            (typeof(KeyboardButtonRingDevice), typeof(IODevice)),
            (typeof(RosenButtonRingDevice), typeof(HidDevice)),
            (typeof(YuanButtonRingDevice), typeof(HidDevice)),
            (typeof(DaoCompositeDevice), typeof(HidDevice)),
            (typeof(RosenTouchPanelDevice), typeof(SerialDevice)),
            (typeof(RosenLedDevice), typeof(SerialDevice)),
            (typeof(PipeButtonRingDevice), typeof(IODevice)),
            (typeof(PipeTouchPanelDevice), typeof(IODevice)),
            (typeof(PipeLedDevice), typeof(IODevice)),
        };
        foreach (var (device, transport) in implementations)
        {
            Check(device.BaseType == transport,
                $"{device.Name} derives directly from {transport.Name}, with no capability-specific base class");
            Check(!device.IsAbstract && typeof(IGameDevice).IsAssignableFrom(device),
                $"{device.Name} is a concrete game device exposing capabilities through interfaces");
        }
        Check(!typeof(IGameDevice).IsAssignableFrom(typeof(IODevice)), "IODevice owns transport lifetime independently of game capabilities");
        foreach (var capability in new[] { typeof(IButtonRingDevice), typeof(ITouchPanelDevice), typeof(ILedDevice) })
        {
            Check(capability.IsInterface && typeof(IGameDevice).IsAssignableFrom(capability),
                $"{capability.Name} extends the common game-device interface");
        }
        Check(typeof(InputStateBuffer).IsSealed && !typeof(IODevice).IsAssignableFrom(typeof(InputStateBuffer)),
            "Input state sharing uses composition instead of another device inheritance layer");
    }

    static void CheckThrows<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, message); return; }
        Check(false, message);
    }

    static void VerifyKeyboardMappings()
    {
        Check(!typeof(KeyboardHelper).IsNested && typeof(KeyboardHelper).IsAbstract && typeof(KeyboardHelper).IsSealed,
            "Keyboard mapping and sampling helper is a standalone static class");
        var logicalKeys = new[]
        {
            KeyCode.B1, KeyCode.B2, KeyCode.B3, KeyCode.B4,
            KeyCode.B5, KeyCode.B6, KeyCode.B7, KeyCode.B8,
            KeyCode.Test, KeyCode.SelectP1, KeyCode.Service, KeyCode.SelectP2,
        };
        var physicalKeys = new[]
        {
            Key.W, Key.E, Key.D, Key.C, Key.X, Key.Z, Key.A, Key.Q,
            Key.Numpad9, Key.NumpadMultiply, Key.Numpad7, Key.Numpad3,
        };
        var gamepadButtons = new[]
        {
            GamepadButton.North, GamepadButton.East, GamepadButton.South, GamepadButton.West,
            GamepadButton.DpadUp, GamepadButton.DpadRight, GamepadButton.DpadDown, GamepadButton.DpadLeft,
            GamepadButton.LeftShoulder, GamepadButton.RightShoulder, GamepadButton.LeftStick, GamepadButton.RightStick,
        };
        Check(KeyboardHelper.GameButtonCount == 8 && KeyboardHelper.ButtonBindings.Length == 12,
            "Mapping distinguishes eight game buttons and four function buttons");
        for (var i = 0; i < logicalKeys.Length; i++)
        {
            var logicalKey = logicalKeys[i];
            Check(KeyboardHelper.ButtonBindings[i] == logicalKey && KeyboardHelper.GetBindingKey((ButtonZone)i) == logicalKey,
                $"ButtonZone index {i} and sampler bindings agree on {logicalKey}");
            Check(KeyboardHelper.ToUnityKeyCode(logicalKey) == physicalKeys[i], $"{logicalKey} keeps its physical keyboard binding");
            Check(KeyboardHelper.ToGamepadButton(logicalKey) == gamepadButtons[i], $"{logicalKey} keeps its gamepad binding");
        }
        CheckThrows<ArgumentOutOfRangeException>(() => KeyboardHelper.GetBindingKey((ButtonZone)(-1)), "Negative button zone rejected");
        CheckThrows<ArgumentOutOfRangeException>(() => KeyboardHelper.GetBindingKey((ButtonZone)12), "Out-of-range button zone rejected");
        CheckThrows<ArgumentOutOfRangeException>(() => KeyboardHelper.ToUnityKeyCode((KeyCode)12), "Unknown keyboard binding rejected");
        CheckThrows<ArgumentOutOfRangeException>(() => KeyboardHelper.ToGamepadButton((KeyCode)12), "Unknown gamepad binding rejected");
    }

    static void VerifyKeyboardSampling()
    {
        Check(!typeof(KeyboardButtonRingDevice).IsNested && typeof(KeyboardButtonRingDevice).Namespace == "MajdataPlay.IO",
            "Keyboard device is standalone rather than nested under InputManager");
        var previousKeyboard = Keyboard.current;
        var previousPolling = MajEnv.Settings.IO.InputDevice.ButtonRing.PollingRateMs;
        try
        {
            var keyboard = new Keyboard();
            Keyboard.current = keyboard;
            var device = new KeyboardButtonRingDevice();
            MajEnv.Settings.IO.InputDevice.ButtonRing.PollingRateMs = 17;
            var interval = (TimeSpan)typeof(KeyboardButtonRingDevice).GetProperty("PollingInterval", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(device)!;
            Check(interval.TotalMilliseconds == 17, "Standalone keyboard reads polling settings without InputManager state");
            keyboard[Key.W].isPressed = true;
            keyboard[Key.Numpad3].isPressed = true;
            device.Tick();
            Check(device.IsButtonCurrentlyOn(0) && device.IsButtonCurrentlyOn(11), "Keyboard samples ring and function keys using shared mappings");
            keyboard[Key.W].isPressed = false;
            device.Tick();
            device.OnPreUpdate();
            var states = new bool[12]; var hadOn = new bool[12]; var hadOff = new bool[12];
            device.ReadButtons(states, hadOn, hadOff);
            Check(!states[0] && hadOn[0] && hadOff[0], "Keyboard press/release pulse survives between snapshots");
            Check(states[11] && hadOn[11] && !hadOff[11], "Keyboard function key is retained in frame state");
            device.ReadButtons(states, hadOn, hadOff);
            Check(hadOn[0] && hadOff[0], "Repeated keyboard reads preserve latched frame events");
            device.OnPreUpdate();
            device.ReadButtons(states, hadOn, hadOff);
            Check(!hadOn[0] && !hadOff[0], "Next keyboard frame clears consumed pulses");
            Keyboard.current = null;
            device.Tick();
            device.OnPreUpdate();
            device.ReadButtons(states, hadOn, hadOff);
            Check(!states[11] && hadOff[11] && !KeyboardHelper.IsKeyDown(KeyCode.B1),
                "Unavailable keyboard releases held keys safely");

            Keyboard.current = keyboard;
            keyboard[Key.W].isPressed = true;
            keyboard[Key.Numpad9].isPressed = true;
            var overlay = new bool[12];
            overlay[9] = true;
            KeyboardHelper.ReadFunctionButtons(overlay);
            Check(!overlay[0] && overlay[8] && overlay[9] && overlay[11],
                "Function overlay merges mapped function keys and preserves hardware input without sampling ring keys");
            device.Tick();
            device.DisconnectForTest();
            Check(!device.IsButtonCurrentlyOn(0) && !device.IsButtonCurrentlyOn(11), "Keyboard disconnect releases live state");
        }
        finally
        {
            Keyboard.current = previousKeyboard;
            MajEnv.Settings.IO.InputDevice.ButtonRing.PollingRateMs = previousPolling;
        }
    }

    static void VerifyButtonProtocols()
    {
        foreach (var player in new[] { 1, 2 })
        {
            IODetector.PlayerIndex = player;
            var device = new RosenButtonRingDevice();
            var report = new byte[player == 1 ? 31 : 33];
            Array.Fill(report, byte.MaxValue);
            var first = player == 1 ? 29 : 31;
            report[first] &= 0xFB;
            device.Feed(report);
            device.Feed(report.AsSpan(0, report.Length - 1));
            Check(device.IsButtonCurrentlyOn(0), $"General P{player} accepts minimum report size and ignores shorter packets");
            device.OnPreUpdate();
            var state = new bool[12]; var on = new bool[12]; var off = new bool[12];
            device.ReadButtons(state, on, off);
            Check(state[0] && on[0] && !off[0], $"General P{player} BA1 active-low mapping");
            for (var i = 1; i < 8; i++) Check(!state[i], $"General P{player} other button {i}");
        }
        IODetector.PlayerIndex = 1;

        var yuan = new YuanButtonRingDevice();
        var yuanReport = new byte[14];
        yuanReport[5] = 1;
        yuan.Feed(yuanReport);
        yuanReport[5] = 0;
        yuan.Feed(yuanReport);
        yuan.OnPreUpdate();
        var states = new bool[12]; var hadOn = new bool[12]; var hadOff = new bool[12];
        yuan.ReadButtons(states, hadOn, hadOff);
        Check(!states[0] && hadOn[0] && hadOff[0], "Yuan pulse between frames retains both edges");
        yuan.ReadButtons(states, hadOn, hadOff);
        Check(hadOn[0] && hadOff[0], "Repeated capability reads do not consume frame edges");
        yuan.OnPreUpdate();
        yuan.ReadButtons(states, hadOn, hadOff);
        Check(!hadOn[0] && !hadOff[0], "Edges clear exactly on the next frame snapshot");
        yuanReport[5] = 1;
        yuan.Feed(yuanReport);
        yuan.Feed(new byte[2]);
        Check(yuan.IsButtonCurrentlyOn(0), "Truncated reports leave latest input unchanged");
        yuan.DisconnectForTest();
        yuan.OnPreUpdate();
        yuan.ReadButtons(states, hadOn, hadOff);
        Check(!states[0], "Disconnect releases pressed buttons");
    }

    static void VerifyHidFunctionButtonOverlay()
    {
        var previousKeyboard = Keyboard.current;
        var keyboard = new Keyboard();
        Keyboard.current = keyboard;
        var devices = new (IODevice Device, byte[] Report, int TestByte, byte TestMask)[]
        {
            (new RosenButtonRingDevice(), new byte[31], 30, 2),
            (new YuanButtonRingDevice(), new byte[14], 11, 1),
            (new DaoCompositeDevice(false), new byte[8], 7, 4),
        };
        try
        {
            foreach (var (device, report, testByte, testMask) in devices)
            {
                var capability = (IButtonRingDevice)device;
                keyboard[Key.Numpad9].isPressed = true;
                device.Feed(report);
                Check(capability.IsButtonCurrentlyOn(8), $"{device.GetType().Name} merges keyboard function input");
                keyboard[Key.Numpad9].isPressed = false;
                device.Feed(report);
                Check(!capability.IsButtonCurrentlyOn(8), $"{device.GetType().Name} releases keyboard function input");
                report[testByte] |= testMask;
                device.Feed(report);
                Check(capability.IsButtonCurrentlyOn(8), $"{device.GetType().Name} retains hardware function input");
            }
        }
        finally { Keyboard.current = previousKeyboard; }
    }

    static void VerifySharedDaoCapabilities()
    {
        var device = new DaoCompositeDevice(false);
        Check(device is IButtonRingDevice && device is ITouchPanelDevice && device is ILedDevice,
            "One Dao instance exposes all three capabilities");
        var report = new byte[8];
        report[1] = 1;
        report[3] = 2;
        report[5] = 128;
        report[6] = 128;
        report[7] = 5;
        device.Feed(report);
        device.Feed(new byte[7]);
        Check(device.IsButtonCurrentlyOn(7) && device.IsSensorCurrentlyOn(33), "Truncated Dao reports preserve both capabilities");
        device.OnPreUpdate();
        var buttons = new bool[12]; var buttonOn = new bool[12]; var buttonOff = new bool[12];
        var touch = new bool[35]; var touchOn = new bool[35]; var touchOff = new bool[35];
        device.ReadButtons(buttons, buttonOn, buttonOff);
        device.ReadTouchPanel(touch, touchOn, touchOff);
        Check(buttons[7] && buttons[8] && buttons[10], "Dao button and function bit mapping");
        Check(touch[0] && touch[17] && touch[33] && !touch[34], "Dao touch A/C/E and reserved bit mapping");
        Check(buttonOn[7] && touchOn[33], "One snapshot publishes both capability edges");
        device.ReadButtons(buttons, buttonOn, buttonOff);
        Check(buttonOn[7], "Touch capability read does not consume button edges");
        device.DisconnectForTest();
        device.OnPreUpdate();
        device.ReadButtons(buttons, buttonOn, buttonOff);
        device.ReadTouchPanel(touch, touchOn, touchOff);
        Check(!buttons[7] && !touch[33], "Shared disconnect releases both capabilities");
    }

    static void VerifySharedDaoPolling()
    {
        var input = MajEnv.Settings.IO.InputDevice;
        var led = MajEnv.Settings.IO.OutputDevice.Led;
        input.ButtonRing.Enable = true;
        input.TouchPanel.Enable = true;
        input.ButtonRing.PollingRateMs = 100;
        input.TouchPanel.PollingRateMs = 50;
        led.RefreshRateMs = 16;
        static double PollingMs(DaoCompositeDevice device) =>
            ((TimeSpan)typeof(DaoCompositeDevice).GetProperty("PollingInterval", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(device)!).TotalMilliseconds;
        Check(PollingMs(new DaoCompositeDevice(true)) == 16, "Combined Dao polling honors fastest LED capability");
        input.TouchPanel.Enable = false;
        Check(PollingMs(new DaoCompositeDevice(false)) == 100, "Button-only Dao uses button polling rate");
        input.ButtonRing.Enable = false;
        input.TouchPanel.Enable = true;
        Check(PollingMs(new DaoCompositeDevice(false)) == 50, "Touch-only Dao uses touch polling rate");
        input.ButtonRing.Enable = true;
        input.ButtonRing.PollingRateMs = 0;
        input.TouchPanel.PollingRateMs = 0;
        led.RefreshRateMs = 0;
    }

    static void VerifySerialTouchFraming()
    {
        var device = new RosenTouchPanelDevice();
        var pressed = new byte[] { (byte)'(', 1, 0, 0, 0, 0, 0, 16, (byte)')' };
        var released = new byte[] { (byte)'(', 0, 0, 0, 0, 0, 0, 0, (byte)')' };
        device.Feed(new byte[] { 4, 7, 9 });
        device.Feed(pressed.AsSpan(0, 4));
        Check(!device.IsSensorCurrentlyOn(0), "Incomplete serial report is not published");
        device.Feed(pressed.AsSpan(4));
        Check(device.IsSensorCurrentlyOn(0) && device.IsSensorCurrentlyOn(34), "Fragmented serial report mapping");
        device.Feed(released);
        device.OnPreUpdate();
        var states = new bool[35]; var hadOn = new bool[35]; var hadOff = new bool[35];
        device.ReadTouchPanel(states, hadOn, hadOff);
        Check(!states[0] && hadOn[0] && hadOff[0], "Serial press and release retain both edges");
        var batch = new byte[18];
        pressed.CopyTo(batch, 0); released.CopyTo(batch, 9);
        device.Feed(batch);
        device.OnPreUpdate();
        device.ReadTouchPanel(states, hadOn, hadOff);
        Check(!states[34] && hadOn[34] && hadOff[34], "Multiple serial frames in one read retain all transitions");
        device.Feed(new byte[] { (byte)'(', 0, 0, 0, (byte)'(', 1, 0, 0, 0 });
        device.Feed(new byte[] { 0, 0, 0, (byte)')' });
        Check(device.IsSensorCurrentlyOn(0), "Serial parser resynchronizes after a corrupt frame");
        device.DisconnectForTest();
        Check(!device.IsSensorCurrentlyOn(0), "Serial disconnect releases live state");
    }

    static byte[] BuildPipeReport(ulong mask)
    {
        Span<byte> payload = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, mask);
        var packet = new PipePacket { Type = PipePacketType.Report, Length = 8, Payload = payload };
        var data = new byte[PipePacket.PACKET_HEADER_LENGTH + 8];
        packet.Write(data);
        return data;
    }

    static void VerifyPipeFraming()
    {
        var touch = new PipeTouchPanelDevice();
        var pressed = BuildPipeReport(1UL | (1UL << 34));
        var released = BuildPipeReport(0);
        for (var i = 0; i < pressed.Length - 1; i++)
        {
            touch.Feed(pressed.AsSpan(i, 1));
            Check(!touch.IsSensorCurrentlyOn(0), "Pipe fragmented frame waits for complete payload");
        }
        touch.Feed(pressed.AsSpan(pressed.Length - 1));
        Check(touch.IsSensorCurrentlyOn(0) && touch.IsSensorCurrentlyOn(34), "Pipe fragmented frame preserves all sensor bits");
        var batch = new byte[pressed.Length + released.Length];
        released.CopyTo(batch, 0); pressed.CopyTo(batch, released.Length);
        touch.Feed(batch);
        touch.OnPreUpdate();
        var states = new bool[35]; var on = new bool[35]; var off = new bool[35];
        touch.ReadTouchPanel(states, on, off);
        Check(states[0] && on[0] && off[0], "Pipe concatenated release/press retains both edges");
        touch.DisconnectForTest();
        touch.OnPreUpdate();
        touch.ReadTouchPanel(states, on, off);
        Check(!states[0] && off[0], "Pipe disconnect releases sensor state");

        var buttons = new PipeButtonRingDevice();
        var report = BuildPipeReport(1UL << 11);
        buttons.Feed(new byte[] { 0x88, 0x48 });
        buttons.Feed(report);
        buttons.OnPreUpdate();
        var buttonStates = new bool[12]; var buttonOn = new bool[12]; var buttonOff = new bool[12];
        buttons.ReadButtons(buttonStates, buttonOn, buttonOff);
        Check(buttonStates[11] && buttonOn[11], "Pipe parser recovers synchronization and maps function buttons");
        buttons.Feed(released.AsSpan(0, 8));
        Check(buttons.IsButtonCurrentlyOn(11), "Partial pipe release does not publish early");
        buttons.Feed(released.AsSpan(8));
        Check(!buttons.IsButtonCurrentlyOn(11), "Fragmented pipe release is published when complete");
    }

    static void VerifySerialLedPackets()
    {
        Span<byte> packet = stackalloc byte[10];
        RosenLedDevice.BuildSetColorPacket(packet, 7, new Color(1, .5f, .25f), .5f);
        Check(packet[0] == 0xE0 && packet[4] == 0x31 && packet[5] == 7, "Serial LED command header and index");
        Check(packet[6] == 127 && packet[7] == 63 && packet[8] == 31, "Serial LED brightness and RGB encoding");
        Check(packet[9] == 44, "Serial LED additive checksum wraps to byte");
        var device = new RosenLedDevice();
        var colors = new Color[8];
        device.WriteLeds(colors, 255);
        device.Tick();
        Check(device.Transport.Writes.Count == 9, "First serial output writes all 8 LEDs and flushes");
        device.Tick();
        Check(device.Transport.Writes.Count == 9, "Unchanged serial output is throttled");
        colors[7] = new Color(1, 0, 0);
        device.WriteLeds(colors, 255);
        device.Tick();
        Check(device.Transport.Writes.Count == 11 && device.Transport.Writes[9][5] == 7,
            "Changed eighth LED writes only its command and flush");
        device.ReconnectForTest();
        device.Tick();
        Check(device.Transport.Writes.Count == 20, "Serial reconnect replays all 8 LEDs");
    }

    static void VerifySharedDeviceFactory()
    {
        var input = MajEnv.Settings.IO.InputDevice;
        var output = MajEnv.Settings.IO.OutputDevice;
        var previousButtonConnection = IODetector.ButtonRingHidConnInfo;
        var previousLedConnection = IODetector.LedDeviceHidConnInfo;
        var previousManufacturer = IODetector.DeviceManufacturer;
        var previousButtonEnabled = input.ButtonRing.Enable;
        var previousTouchEnabled = input.TouchPanel.Enable;
        var previousLedEnabled = output.Led.Enable;
        var previousButtonPolling = input.ButtonRing.PollingRateMs;
        var previousTouchPolling = input.TouchPanel.PollingRateMs;
        var previousLedRefresh = output.Led.RefreshRateMs;
        try
        {
            input.ButtonRing.PollingRateMs = 100;
            input.TouchPanel.PollingRateMs = 100;
            output.Led.RefreshRateMs = 100;
            input.ButtonRing.Enable = true;
            input.TouchPanel.Enable = true;
            output.Led.Enable = true;
            IODetector.DeviceManufacturer = MajdataPlay.Settings.DeviceManufacturerOption.Dao;
            IODetector.ButtonRingHidConnInfo = new()
            {
                VendorId = 0x1234, ProductId = 0x5678, DeviceName = "button settings",
                Exclusice = false, OpenPriority = 1,
            };
            IODetector.LedDeviceHidConnInfo = new()
            {
                VendorId = 0x4321, ProductId = 0x8765, DeviceName = "LED settings",
                Exclusice = true, OpenPriority = 2,
            };
            ResetDeviceFactory();
            var createdBefore = HidDevice.CreatedCount;
            GameDeviceManager.Init();
            Check(HidDevice.CreatedCount == createdBefore + 1 && RegisteredDeviceCount() == 1,
                "Different Dao connection and open options still create exactly one physical device");
            Check(ReferenceEquals(GameDeviceManager.ButtonRing, GameDeviceManager.TouchPanel), "Factory shares Dao buttons and touch on one instance");
            Check(ReferenceEquals(GameDeviceManager.ButtonRing, GameDeviceManager.LedDevice), "Factory shares Dao LED despite different connection options");
            var device = (DaoCompositeDevice)GameDeviceManager.ButtonRing!;
            Check(device.Connection.Equals(IODetector.ButtonRingHidConnInfo), "Combined Dao uses input connection settings");
            Check(SpinWait.SpinUntil(() => device.ReadCount > 0 && device.Transport.WriteCount > 0, 2000),
                "Combined Dao performs input reads and LED writes");
            Check(device.ConnectCount == 1, "Combined Dao opens its HID connection once");
            var report = new byte[8];
            report[1] = 1;
            report[6] = 1;
            device.Feed(report);
            GameDeviceManager.OnPreUpdate();
            var buttons = new bool[12]; var on = new bool[12]; var off = new bool[12];
            var sensors = new bool[35]; var sensorOn = new bool[35]; var sensorOff = new bool[35];
            device.ReadButtons(buttons, on, off);
            device.ReadTouchPanel(sensors, sensorOn, sensorOff);
            Check(buttons[0] && on[0] && sensors[0] && sensorOn[0], "Factory snapshots a shared device once without clearing its first-frame edges");
            GameDeviceManager.Init();
            Check(ReferenceEquals(GameDeviceManager.ButtonRing, device) && HidDevice.CreatedCount == createdBefore + 1,
                "Factory initialization is idempotent");
            ResetDeviceFactory();

            input.ButtonRing.Enable = false;
            input.TouchPanel.Enable = false;
            createdBefore = HidDevice.CreatedCount;
            GameDeviceManager.Init();
            Check(GameDeviceManager.ButtonRing == null && GameDeviceManager.TouchPanel == null,
                "LED-only Dao does not register disabled input capabilities");
            var ledOnly = GameDeviceManager.LedDevice as DaoCompositeDevice;
            Check(ledOnly != null && HidDevice.CreatedCount == createdBefore + 1 && RegisteredDeviceCount() == 1,
                "LED-only Dao uses the same concrete combined device with one instance");
            Check(ledOnly!.Connection.Equals(IODetector.LedDeviceHidConnInfo), "LED-only Dao preserves LED connection settings");
            Check(SpinWait.SpinUntil(() => ledOnly.Transport.WriteCount > 0, 2000), "LED-only Dao sends output");
            ResetDeviceFactory();
            Check(ledOnly.ReadCount == 0 && ledOnly.ConnectCount == 1, "LED-only Dao opens once without reading input");

            input.ButtonRing.Enable = true;
            input.TouchPanel.Enable = true;
            output.Led.Enable = false;
            createdBefore = HidDevice.CreatedCount;
            GameDeviceManager.Init();
            var inputOnly = (DaoCompositeDevice)GameDeviceManager.ButtonRing!;
            Check(GameDeviceManager.LedDevice == null && ReferenceEquals(inputOnly, GameDeviceManager.TouchPanel),
                "Input-only Dao registers inputs and leaves LED capability disabled");
            Check(HidDevice.CreatedCount == createdBefore + 1 && RegisteredDeviceCount() == 1,
                "Input-only Dao creates exactly one device");
            Check(inputOnly.Connection.Equals(IODetector.ButtonRingHidConnInfo), "Input-only Dao selects input connection settings");
            Check(SpinWait.SpinUntil(() => inputOnly.ReadCount > 0, 2000), "Input-only Dao reads reports");
            ResetDeviceFactory();
            Check(inputOnly.Transport.WriteCount == 0 && inputOnly.ConnectCount == 1,
                "Input-only Dao opens once without writing LEDs");

            input.ButtonRing.Enable = false;
            input.TouchPanel.Enable = false;
            createdBefore = HidDevice.CreatedCount;
            GameDeviceManager.Init();
            Check(GameDeviceManager.ButtonRing == null && GameDeviceManager.TouchPanel == null && GameDeviceManager.LedDevice == null,
                "Disabled Dao capabilities register no device");
            Check(HidDevice.CreatedCount == createdBefore && RegisteredDeviceCount() == 0,
                "All-disabled Dao creates no HID connection");
        }
        finally
        {
            ResetDeviceFactory();
            input.ButtonRing.Enable = previousButtonEnabled;
            input.TouchPanel.Enable = previousTouchEnabled;
            output.Led.Enable = previousLedEnabled;
            input.ButtonRing.PollingRateMs = previousButtonPolling;
            input.TouchPanel.PollingRateMs = previousTouchPolling;
            output.Led.RefreshRateMs = previousLedRefresh;
            IODetector.DeviceManufacturer = previousManufacturer;
            IODetector.ButtonRingHidConnInfo = previousButtonConnection;
            IODetector.LedDeviceHidConnInfo = previousLedConnection;
        }
    }

    static List<IGameDevice> RegisteredDevices() =>
        (List<IGameDevice>)typeof(GameDeviceManager).GetField("_devices", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    static int RegisteredDeviceCount()
    {
        var count = 0;
        foreach (var device in RegisteredDevices())
            if (device is IODevice) count++;
        return count;
    }

    static void ResetDeviceFactory()
    {
        var devices = RegisteredDevices();
        foreach (var device in devices)
            if (device is IODevice io) io.Dispose();
        devices.Clear();
        foreach (var name in new[] { "ButtonRing", "TouchPanel", "LedDevice" })
            typeof(GameDeviceManager).GetProperty(name, BindingFlags.Public | BindingFlags.Static)!.SetValue(null, null);
        typeof(GameDeviceManager).GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, false);
    }

    static void VerifyDaoLedPackets()
    {
        var colors = new Color[8];
        for (var i = 0; i < colors.Length; i++) colors[i] = new Color((i + 1) / 8f, .5f, .25f);
        Span<byte> packet = stackalloc byte[64];
        packet.Fill(255);
        DaoLedOutput.BuildUpdatePacket(packet, 7, colors, 128, .5f);
        Check(packet[0] == 7 && packet[25] == 64, "Dao report ID and cabinet brightness");
        for (var i = 0; i < 8; i++)
        {
            Check(packet[1 + i * 3] == (byte)((i + 1) / 8f * 127.5f), $"Dao LED {i + 1} red");
            Check(packet[2 + i * 3] == 63 && packet[3 + i * 3] == 31, $"Dao LED {i + 1} green/blue");
        }
        for (var i = 26; i < packet.Length; i++) Check(packet[i] == 0, $"Dao padding {i} cleared");
        var output = new DaoLedOutput();
        var stream = new HidSharp.HidStream();
        var device = new HidSharp.HidDevice();
        output.WriteLeds(colors, 128);
        output.WriteTo(stream, device);
        output.WriteTo(stream, device);
        Check(stream.Writes.Count == 1, "Dao unchanged output throttles after initial write");
        output.WriteLeds(colors, 129);
        output.WriteTo(stream, device);
        Check(stream.Writes.Count == 2 && stream.Writes[1][25] == 129, "Dao cabinet-only change is sent");
        output.Reset();
        output.WriteTo(stream, device);
        Check(stream.Writes.Count == 3, "Dao reconnect replays unchanged colors");
        Check(stream.Writes[0].Length == device.OutputReportLength, "Dao uses output report size");
    }
}
