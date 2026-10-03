using System;
using System.Collections.Generic;
using MajdataPlay.Settings;

#nullable enable
namespace MajdataPlay.IO
{
    /// <summary>Owns input and output devices; capabilities may refer to the same instance.</summary>
    internal static class GameDeviceManager
    {
        static bool _initialized;
        static readonly List<IGameDevice> _devices = new List<IGameDevice>();
        public static IButtonRingDevice? ButtonRing { get; private set; }
        public static ITouchPanelDevice? TouchPanel { get; private set; }
        public static ILedDevice? LedDevice { get; private set; }
        public static ScreenTouchPanelDevice ScreenTouchPanel { get; } = new ScreenTouchPanelDevice();
        public static MouseTouchPanelDevice MouseTouchPanel { get; } = new MouseTouchPanelDevice();

        public static void Init()
        {
            if (_initialized)
            {
                return;
            }
            _initialized = true;
#if UNITY_STANDALONE
            var input = MajEnv.Settings.IO.InputDevice;
            var output = MajEnv.Settings.IO.OutputDevice;
            var manufacturer = IODetector.DeviceManufacturer;
            if (manufacturer == DeviceManufacturerOption.Dao)
            {
                if (input.ButtonRing.Enable || input.TouchPanel.Enable || output.Led.Enable)
                {
                    var device = new DaoCompositeDevice(output.Led.Enable);
                    if (input.ButtonRing.Enable)
                    {
                        ButtonRing = device;
                    }
                    if (input.TouchPanel.Enable)
                    {
                        TouchPanel = device;
                    }
                    if (output.Led.Enable)
                    {
                        LedDevice = device;
                    }
                }
            }
            else
            {
                if (input.ButtonRing.Enable)
                {
                    switch (manufacturer)
                    {
                        case DeviceManufacturerOption.General:
                        case DeviceManufacturerOption.Nov:
                            ButtonRing = IODetector.ButtonRingDevice == ButtonRingDeviceOption.Keyboard
                                ? new KeyboardButtonRingDevice() : new RosenButtonRingDevice();
                            break;
                        case DeviceManufacturerOption.Yuan: ButtonRing = new YuanButtonRingDevice(); break;
                        case DeviceManufacturerOption.Pipe: ButtonRing = new PipeButtonRingDevice(); break;
                    }
                }
                if (input.TouchPanel.Enable)
                {
                    switch (manufacturer)
                    {
                        case DeviceManufacturerOption.General:
                        case DeviceManufacturerOption.Yuan: TouchPanel = new RosenTouchPanelDevice(); break;
                        case DeviceManufacturerOption.Pipe: TouchPanel = new PipeTouchPanelDevice(); break;
#if UNITY_STANDALONE_WIN
                        case DeviceManufacturerOption.Nov: TouchPanel = new ExclusiveTouchHost(); break;
#endif
                    }
                }
                if (output.Led.Enable)
                {
                    switch (manufacturer)
                    {
                        case DeviceManufacturerOption.General:
                        case DeviceManufacturerOption.Yuan:
                        case DeviceManufacturerOption.Nov: LedDevice = new RosenLedDevice(); break;
                        case DeviceManufacturerOption.Pipe: LedDevice = new PipeLedDevice(); break;
                    }
                }
            }
#elif UNITY_ANDROID || UNITY_IOS
            switch (MajEnv.Settings.IO.InputDevice.ExternalButtonRing)
            {
                case MobileExternalButtonRingOption.Keyboard:
#if UNITY_ANDROID
                    ButtonRing = new AndroidKeyboardButtonRingDevice();
#else
                    ButtonRing = new IOSKeyboardButtonRingDevice();
#endif
                    break;
                case MobileExternalButtonRingOption.Gamepad:
                    ButtonRing = new GamepadButtonRingDevice();
                    break;
            }
#endif
            Register(ButtonRing);
            Register(TouchPanel);
            Register(LedDevice);
            Register(ScreenTouchPanel);
            Register(MouseTouchPanel);
#if UNITY_STANDALONE
            foreach (var device in _devices)
            {
                if (device is IODevice io)
                {
                    io.Start();
                }
            }
#endif
        }

        static void Register(IGameDevice? device)
        {
            if (device == null)
            {
                return;
            }
            foreach (var registered in _devices)
            {
                if (ReferenceEquals(registered, device))
                {
                    return;
                }
            }
            _devices.Add(device);
        }

        public static void OnPreUpdate()
        {
            // Snapshot each device once even when several facades consume it.
            foreach (var device in _devices)
            {
                device.OnPreUpdate();
            }
        }

    }
}
