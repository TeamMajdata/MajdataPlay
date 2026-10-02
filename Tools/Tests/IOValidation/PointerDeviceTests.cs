using System;
using MajdataPlay;
using MajdataPlay.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

internal static partial class Program
{
    static Touch Contact(int id, int position, bool ended = false) => new()
    {
        valid = true,
        touchId = id,
        screenPosition = new Vector2(position, 0),
        radius = new Vector2(1, 0),
        ended = ended,
    };

    static void ResetPointerInputs()
    {
        Touchscreen.current = null;
        Mouse.current = null;
        Touch.activeTouches = Array.Empty<Touch>();
        SceneSwitcher.MainCamera = new Camera();
        ScreenTouchMapper.IsInitialized = true;
        ScreenTouchMapper.UseOuterTouchAsSensor = false;
        ScreenTouchMapper.SampleCount = 0;
        ScreenTouchMapper.PositionReports.Clear();
    }

    static void VerifyPointerDevices()
    {
        ResetPointerInputs();
        try
        {
            var touch = new ScreenTouchPanelDevice();
            Check(touch is IButtonRingDevice && touch is ITouchPanelDevice,
                "Screen touch exposes button and sensor capabilities on one instance");
            Check(touch.ButtonClickedCount.Length == 8 && touch.SensorClickedCount.Length == 34,
                "Pointer counts retain eight buttons and both center sensor halves");
            Touchscreen.current = new Touchscreen();
            ScreenTouchMapper.PositionReports[1] = (1UL << (12 + 16)) | (1UL << (12 + 17));
            ScreenTouchMapper.PositionReports[2] = (1UL << 7) | (1UL << (12 + 33));
            Touch.activeTouches = new[] { Contact(101, 1), Contact(102, 1), Contact(103, 2) };
            touch.OnPreUpdate();
            Check(touch.IsConnected && touch.IsSensorCurrentlyOn(16) && touch.IsSensorCurrentlyOn(17) &&
                touch.IsSensorCurrentlyOn(33) && touch.IsButtonCurrentlyOn(7),
                "Screen touch ORs simultaneous pointers into button and sensor state");
            Check(touch.SensorClickedCount[16] == 2 && touch.SensorClickedCount[17] == 2 &&
                touch.SensorClickedCount[33] == 1 && touch.ButtonClickedCount[7] == 1,
                "Overlapping fingers preserve separate click counts including both center halves");
            Check(!touch.IsSensorCurrentlyOn(34), "Pointer sampling leaves reserved sensor bit off");
            var buttons = new bool[12]; var buttonOn = new bool[12]; var buttonOff = new bool[12];
            var sensors = new bool[35]; var sensorOn = new bool[35]; var sensorOff = new bool[35];
            touch.ReadButtons(buttons, buttonOn, buttonOff);
            touch.ReadTouchPanel(sensors, sensorOn, sensorOff);
            Check(buttonOn[7] && sensorOn[16] && touch.SensorClickedCount[16] == 2,
                "Reading both pointer capabilities preserves the shared frame and counts");
            touch.OnPreUpdate();
            Check(touch.SensorClickedCount[16] == 0 && touch.ButtonClickedCount[7] == 0 && touch.IsSensorCurrentlyOn(16),
                "Held contacts stay pressed without repeating clicks");
            Touch.activeTouches = Array.Empty<Touch>();
            touch.OnPreUpdate();
            touch.ReadTouchPanel(sensors, sensorOn, sensorOff);
            Check(!sensors[16] && sensorOff[16] && !touch.IsButtonCurrentlyOn(7),
                "A frame with no contacts releases all pointer state");
            Touch.activeTouches = new[] { Contact(101, 1) };
            touch.OnPreUpdate();
            Check(touch.SensorClickedCount[16] == 1, "Reused touch ID clicks again after an absent frame");
            Touch.activeTouches = new[] { Contact(101, 1, ended: true) };
            touch.OnPreUpdate();
            Check(touch.IsSensorCurrentlyOn(16), "Ended contact retains its final position for that frame");
            Touch.activeTouches = new[] { Contact(101, 1) };
            touch.OnPreUpdate();
            Check(touch.SensorClickedCount[16] == 1, "Ended contact removes its previous ID history");
            Touch.activeTouches = new[] { new Touch { valid = false, touchId = 101, screenPosition = new Vector2(1, 0) } };
            touch.OnPreUpdate();
            Check(!touch.IsSensorCurrentlyOn(16) && touch.SensorClickedCount[16] == 0,
                "Invalid contacts release prior state and do not create counts");

            Touch.activeTouches = new[] { Contact(1, 2) };
            touch.OnPreUpdate();
            SceneSwitcher.MainCamera = null;
            touch.OnPreUpdate();
            Check(!touch.IsButtonCurrentlyOn(7) && touch.ButtonClickedCount[7] == 0,
                "Unavailable camera clears pointer state before returning");
            SceneSwitcher.MainCamera = new Camera();
            touch.OnPreUpdate();
            ScreenTouchMapper.IsInitialized = false;
            touch.OnPreUpdate();
            Check(!touch.IsSensorCurrentlyOn(33) && touch.SensorClickedCount[33] == 0,
                "Unavailable position map clears pointer state");
            ScreenTouchMapper.IsInitialized = true;
            Touchscreen.current = null;
            Touch.activeTouches = Array.Empty<Touch>();
            touch.OnPreUpdate();
            Check(!touch.IsConnected && !touch.IsButtonCurrentlyOn(7), "Touchscreen removal releases both capabilities");

            VerifyPointerBoundaryCounts();
            VerifyMousePriority();
        }
        finally { ResetPointerInputs(); }
    }

    static void VerifyPointerBoundaryCounts()
    {
        Touchscreen.current = new Touchscreen();
        ScreenTouchMapper.PositionReports[10] = 1UL;
        ScreenTouchMapper.PositionReports[11] = 1UL << 12;
        var touch = new ScreenTouchPanelDevice();
        Touch.activeTouches = new[] { Contact(55, 10) };
        touch.OnPreUpdate();
        Check(touch.ButtonClickedCount[0] == 1, "Outer ring pointer begins with one button click");
        Touch.activeTouches = new[] { Contact(55, 11) };
        touch.OnPreUpdate();
        Check(touch.IsSensorCurrentlyOn(0) && touch.SensorClickedCount[0] == 0 && !touch.IsButtonCurrentlyOn(0),
            "Moving from ring button into matching sensor does not count a second click");
        Touch.activeTouches = new[] { Contact(55, 10) };
        touch.OnPreUpdate();
        Check(touch.IsButtonCurrentlyOn(0) && touch.ButtonClickedCount[0] == 0,
            "Moving back into matching ring button does not count a second click");
        Touch.activeTouches = Array.Empty<Touch>();
        touch.OnPreUpdate();
        ScreenTouchMapper.UseOuterTouchAsSensor = true;
        Touch.activeTouches = new[] { Contact(55, 10) };
        touch.OnPreUpdate();
        Check(!touch.IsButtonCurrentlyOn(0) && touch.IsSensorCurrentlyOn(0) &&
            touch.SensorClickedCount[0] == 1 && touch.ButtonClickedCount[0] == 0,
            "Outer-touch sensor mode preserves sensor mapping and counts");
        ScreenTouchMapper.UseOuterTouchAsSensor = false;
        touch.OnPreUpdate();
        Check(touch.IsSensorCurrentlyOn(0) && !touch.IsButtonCurrentlyOn(0) && touch.SensorClickedCount[0] == 0,
            "An ongoing sensor-only gesture keeps its classification without recounting");
        Touch.activeTouches = Array.Empty<Touch>();
    }

    static void VerifyMousePriority()
    {
        var mouse = new Mouse();
        Mouse.current = mouse;
        mouse.leftButton.isPressed = true;
        mouse.position.value = new Vector2(2, 0);
        var device = new MouseTouchPanelDevice();
        Check(device is IButtonRingDevice && device is ITouchPanelDevice,
            "Mouse exposes button and sensor capabilities on one instance");
        device.OnPreUpdate();
        Check(device.IsConnected && device.IsButtonCurrentlyOn(7) && device.IsSensorCurrentlyOn(33),
            "Mouse contributes both mapped capabilities when no touch is active");
        Check(device.ButtonClickedCount[7] == 1 && device.SensorClickedCount[33] == 1,
            "Mouse starts with one click per mapped zone");
        device.OnPreUpdate();
        Check(device.ButtonClickedCount[7] == 0 && device.SensorClickedCount[33] == 0,
            "Held mouse does not repeatedly count clicks");
        Touch.activeTouches = new[] { Contact(77, 1) };
        device.OnPreUpdate();
        Check(!device.IsButtonCurrentlyOn(7) && !device.IsSensorCurrentlyOn(33) && device.ButtonClickedCount[7] == 0,
            "Active touchscreen input takes priority and releases mouse contributions");
        Touch.activeTouches = Array.Empty<Touch>();
        device.OnPreUpdate();
        Check(device.IsButtonCurrentlyOn(7) && device.ButtonClickedCount[7] == 1,
            "Mouse resumes with a fresh click after touch-priority suppression");
        mouse.leftButton.isPressed = false;
        device.OnPreUpdate();
        Check(!device.IsButtonCurrentlyOn(7) && !device.IsSensorCurrentlyOn(33), "Mouse release clears both mapped capabilities");
        mouse.leftButton.isPressed = true;
        device.OnPreUpdate();
        Mouse.current = null;
        device.OnPreUpdate();
        var sensors = new bool[35]; var on = new bool[35]; var off = new bool[35];
        device.ReadTouchPanel(sensors, on, off);
        Check(!device.IsConnected && !sensors[33] && off[33], "Mouse disconnect publishes releases");
    }

    static void VerifySupplementalDeviceFactory()
    {
        var input = MajEnv.Settings.IO.InputDevice;
        var output = MajEnv.Settings.IO.OutputDevice;
        var original = (input.ButtonRing.Enable, input.TouchPanel.Enable, output.Led.Enable, IODetector.DeviceManufacturer);
        ResetPointerInputs();
        ResetDeviceFactory();
        try
        {
            input.ButtonRing.Enable = false;
            input.TouchPanel.Enable = false;
            output.Led.Enable = false;
            IODetector.DeviceManufacturer = MajdataPlay.Settings.DeviceManufacturerOption.Dao;
            GameDeviceManager.Init();
            GameDeviceManager.Init();
            var registrations = RegisteredDevices();
            Check(registrations.Count == 2 && ReferenceEquals(registrations[0], GameDeviceManager.ScreenTouchPanel) &&
                ReferenceEquals(registrations[1], GameDeviceManager.MouseTouchPanel),
                "Factory registers each supplemental device once even though both expose two capabilities");
            Check(RegisteredDeviceCount() == 0 && GameDeviceManager.ButtonRing == null && GameDeviceManager.TouchPanel == null,
                "Supplemental devices remain available with physical inputs disabled");
            Touchscreen.current = new Touchscreen();
            var mouse = new Mouse();
            Mouse.current = mouse;
            mouse.leftButton.isPressed = true;
            mouse.position.value = new Vector2(2, 0);
            ScreenTouchMapper.PositionReports[1] = (1UL << 12) | 1UL;
            ScreenTouchMapper.PositionReports[2] = (1UL << 45) | (1UL << 7);
            Touch.activeTouches = new[] { Contact(900, 1) };
            ScreenTouchMapper.SampleCount = 0;
            GameDeviceManager.OnPreUpdate();
            Check(ScreenTouchMapper.SampleCount == 1 && GameDeviceManager.ScreenTouchPanel.ButtonClickedCount[0] == 1 &&
                GameDeviceManager.ScreenTouchPanel.SensorClickedCount[0] == 1,
                "Factory samples a touchscreen contact once and preserves its first-frame counts");
            Check(GameDeviceManager.ScreenTouchPanel.IsButtonCurrentlyOn(0) &&
                !GameDeviceManager.MouseTouchPanel.IsButtonCurrentlyOn(7),
                "Factory preserves touchscreen priority over a pressed mouse");
            GameDeviceManager.OnPreUpdate();
            Check(ScreenTouchMapper.SampleCount == 2 && GameDeviceManager.ScreenTouchPanel.ButtonClickedCount[0] == 0,
                "Next factory frame samples once and retains held-contact history");
            Touch.activeTouches = Array.Empty<Touch>();
            GameDeviceManager.OnPreUpdate();
            Check(ScreenTouchMapper.SampleCount == 3 && !GameDeviceManager.ScreenTouchPanel.IsButtonCurrentlyOn(0) &&
                GameDeviceManager.MouseTouchPanel.IsButtonCurrentlyOn(7) && GameDeviceManager.MouseTouchPanel.ButtonClickedCount[7] == 1,
                "Factory releases touchscreen state and samples resumed mouse once");
        }
        finally
        {
            ResetPointerInputs();
            GameDeviceManager.OnPreUpdate();
            ResetDeviceFactory();
            (input.ButtonRing.Enable, input.TouchPanel.Enable, output.Led.Enable, IODetector.DeviceManufacturer) = original;
        }
    }
}
