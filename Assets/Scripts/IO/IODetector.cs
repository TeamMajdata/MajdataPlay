using MajdataPlay.Diagnostics;
using MajdataPlay.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
#if UNITY_STANDALONE
using HidSharp;
#endif

namespace MajdataPlay.IO
{
    public static class IODetector
    {
        const int YUAN_HID_1P_PID = 22352;
        const int YUAN_HID_1P_VID = 11836;
        const int YUAN_HID_2P_PID = 22352;
        const int YUAN_HID_2P_VID = 11852;
        const int DAO_HID_PID = 4644;
        const int DAO_HID_VID = 3727;
        const int NOV_USB_PID = 0x3003;
        const int NOV_USB_VID = 0x3356;
        const int FL_USB_PID = 0x0103;
        const int FL_USB_VID = 0x227D;
        const int GENERAL_HID_1P_PID = 33;
        const int GENERAL_HID_1P_VID = 3235;
        const int GENERAL_HID_2P_PID = 33;
        const int GENERAL_HID_2P_VID = 3235;

        public static int PlayerIndex { get; private set; } = 1;
        public static DeviceManufacturerOption DeviceManufacturer { get; private set; } = DeviceManufacturerOption.General;
        public static ButtonRingDeviceOption ButtonRingDevice { get; private set; } = ButtonRingDeviceOption.Keyboard;

        public static SerialPortConnInfo TouchPanelSerialConnInfo { get; private set; }
        public static SerialPortConnInfo LedDeviceSerialConnInfo { get; private set; }
        public static HidConnInfo LedDeviceHidConnInfo { get; private set; }
        public static HidConnInfo ButtonRingHidConnInfo { get; private set; }
        public static UsbConnInfo TouchPanelUsbConnInfo { get; private set; }

        static bool _isInited = false;

        [Conditional("UNITY_STANDALONE")]
        public static void Init()
        {
#if UNITY_STANDALONE
            if (_isInited)
            {
                return;
            }
            var hidDevices = HidHelper.Devices;
#if UNITY_STANDALONE_WIN
            // 独占触摸只走 WinUSB，只有绑定到 winusb.sys 的设备才可用
            var hasNovUsbDevice = HasWinUsbDevice(NOV_USB_VID, NOV_USB_PID) ||
                                  HasWinUsbDevice(FL_USB_VID, FL_USB_PID);
#else
            var hasNovUsbDevice = false;
#endif
#if ENABLE_IL2CPP
            var serialPorts = "NotSupported";
#else
            var serialPorts = DeviceList.Local.GetSerialDevices();
#endif

            var ioSettings = MajEnv.Settings.IO;
            var playerIndex = ioSettings.InputDevice.Player;
            var buttonRingSettings = ioSettings.InputDevice.ButtonRing;
            var touchPanelSettings = ioSettings.InputDevice.TouchPanel;
            var ledDeviceSettings = ioSettings.OutputDevice.Led;
            var userManufacturer = ioSettings.Manufacturer;
            var userButtonRingType = buttonRingSettings.Type;

            var manufacturer = DeviceManufacturerOption.General;
            var buttonRingType = userButtonRingType ?? ButtonRingDeviceOption.Keyboard;

            MajDebug.LogInfo($"Nov USB device present: {hasNovUsbDevice}");
            MajDebug.LogInfo($"All available HID devices:\n{string.Join('\n', hidDevices)}");
            MajDebug.LogInfo($"All available serial ports:\n{string.Join('\n', serialPorts)}");

            try
            {
                if (userButtonRingType is not null && buttonRingType == ButtonRingDeviceOption.Keyboard)
                {
                    manufacturer = DeviceManufacturerOption.General;

                    DeviceManufacturer = manufacturer;
                    ButtonRingDevice = buttonRingType;
                    TouchPanelSerialConnInfo = new()
                    {
#if UNITY_STANDALONE_WIN
                        PortName = "COM" + (touchPanelSettings.SerialPortOptions.Port ?? (playerIndex == 1 ? 3 : 4)),
#else
                        PortName = touchPanelSettings.SerialPortOptions.Port ?? WinSerialPortToLinuxPortName((playerIndex == 1 ? 3 : 4)),
#endif
                        BaudRate = touchPanelSettings.SerialPortOptions.BaudRate ?? 9600,
                    };
                    LedDeviceSerialConnInfo = new()
                    {
#if UNITY_STANDALONE_WIN
                        PortName = "COM" + (ledDeviceSettings.SerialPortOptions.Port ?? (playerIndex == 1 ? 21 : 22)),
#else
                        PortName = ledDeviceSettings.SerialPortOptions.Port ?? WinSerialPortToLinuxPortName((playerIndex == 1 ? 21 : 22)),
#endif
                        BaudRate = ledDeviceSettings.SerialPortOptions.BaudRate ?? 115200,
                    };
                    return;
                }
                else if (userManufacturer is not null)
                {
                    MajDebug.LogInfo("User has set the IO manufacturer and button ring type, use the user-set values");
                    manufacturer = (DeviceManufacturerOption)userManufacturer;
                    switch (manufacturer)
                    {
                        case DeviceManufacturerOption.General:
                            buttonRingType = ButtonRingDeviceOption.HID;
                            TouchPanelSerialConnInfo = new()
                            {
#if UNITY_STANDALONE_WIN
                                PortName = "COM" + (touchPanelSettings.SerialPortOptions.Port ?? (playerIndex == 1 ? 3 : 4)),
#else
                                PortName = touchPanelSettings.SerialPortOptions.Port ?? WinSerialPortToLinuxPortName((playerIndex == 1 ? 3 : 4)),
#endif
                                BaudRate = touchPanelSettings.SerialPortOptions.BaudRate ?? 9600,
                            };
                            ButtonRingHidConnInfo = new()
                            {
                                DeviceName = buttonRingSettings.HidOptions.DeviceName ?? string.Empty,
                                ProductId = buttonRingSettings.HidOptions.ProductId ?? (playerIndex == 1 ? GENERAL_HID_1P_PID : GENERAL_HID_2P_PID),
                                VendorId = buttonRingSettings.HidOptions.VendorId ?? (playerIndex == 1 ? GENERAL_HID_1P_VID : GENERAL_HID_2P_VID),
                                Exclusice = buttonRingSettings.HidOptions.Exclusice,
                                OpenPriority = buttonRingSettings.HidOptions.OpenPriority
                            };
                            LedDeviceSerialConnInfo = new()
                            {
#if UNITY_STANDALONE_WIN
                                PortName = "COM" + (ledDeviceSettings.SerialPortOptions.Port ?? (playerIndex == 1 ? 21 : 22)),
#else
                                PortName = ledDeviceSettings.SerialPortOptions.Port ?? WinSerialPortToLinuxPortName((playerIndex == 1 ? 21 : 22)),
#endif
                                BaudRate = ledDeviceSettings.SerialPortOptions.BaudRate ?? 115200,
                            };
                            break;
                        case DeviceManufacturerOption.Yuan:
                            buttonRingType = ButtonRingDeviceOption.HID;
                            TouchPanelSerialConnInfo = new()
                            {
#if UNITY_STANDALONE_WIN
                                PortName = "COM" + (touchPanelSettings.SerialPortOptions.Port ?? (playerIndex == 1 ? 3 : 4)),
#else
                                PortName = touchPanelSettings.SerialPortOptions.Port ?? WinSerialPortToLinuxPortName((playerIndex == 1 ? 3 : 4)),
#endif
                                BaudRate = touchPanelSettings.SerialPortOptions.BaudRate ?? 9600,
                            };
                            ButtonRingHidConnInfo = new()
                            {
                                DeviceName = buttonRingSettings.HidOptions.DeviceName ?? string.Empty,
                                ProductId = buttonRingSettings.HidOptions.ProductId ?? (playerIndex == 1 ? YUAN_HID_1P_PID : YUAN_HID_2P_PID),
                                VendorId = buttonRingSettings.HidOptions.VendorId ?? (playerIndex == 1 ? YUAN_HID_1P_VID : YUAN_HID_2P_VID),
                                Exclusice = buttonRingSettings.HidOptions.Exclusice,
                                OpenPriority = buttonRingSettings.HidOptions.OpenPriority
                            };
                            LedDeviceSerialConnInfo = new()
                            {
#if UNITY_STANDALONE_WIN
                                PortName = "COM" + (ledDeviceSettings.SerialPortOptions.Port ?? (playerIndex == 1 ? 21 : 22)),
#else
                                PortName = ledDeviceSettings.SerialPortOptions.Port ?? WinSerialPortToLinuxPortName((playerIndex == 1 ? 21 : 22)),
#endif
                                BaudRate = ledDeviceSettings.SerialPortOptions.BaudRate ?? 115200,
                            };
                            break;
                        case DeviceManufacturerOption.Dao:
                            buttonRingType = ButtonRingDeviceOption.HID;
                            ButtonRingHidConnInfo = new()
                            {
                                DeviceName = buttonRingSettings.HidOptions.DeviceName ?? string.Empty,
                                ProductId = buttonRingSettings.HidOptions.ProductId ?? DAO_HID_PID,
                                VendorId = buttonRingSettings.HidOptions.VendorId ?? DAO_HID_VID,
                                Exclusice = buttonRingSettings.HidOptions.Exclusice,
                                OpenPriority = buttonRingSettings.HidOptions.OpenPriority
                            };
                            LedDeviceHidConnInfo = new()
                            {
                                DeviceName = ledDeviceSettings.HidOptions.DeviceName ?? string.Empty,
                                ProductId = ledDeviceSettings.HidOptions.ProductId ?? DAO_HID_PID,
                                VendorId = ledDeviceSettings.HidOptions.VendorId ?? DAO_HID_VID,
                                Exclusice = ledDeviceSettings.HidOptions.Exclusice,
                                OpenPriority = ledDeviceSettings.HidOptions.OpenPriority
                            };
                            break;
                        case DeviceManufacturerOption.Nov:
                            buttonRingType = ButtonRingDeviceOption.Keyboard;
                            TouchPanelUsbConnInfo = new()
                            {
                                DeviceName = touchPanelSettings.UsbOptions.DeviceName ?? string.Empty,
                                ProductId = touchPanelSettings.UsbOptions.ProductId ?? NOV_USB_PID,
                                VendorId = touchPanelSettings.UsbOptions.VendorId ?? NOV_USB_VID,
                                Configuration = 1,
                                Interface = 1,
                                PacketSize = 64
                            };
                            LedDeviceSerialConnInfo = new()
                            {
#if UNITY_STANDALONE_WIN
                                PortName = "COM" + (ledDeviceSettings.SerialPortOptions.Port ?? (playerIndex == 1 ? 21 : 22)),
#else
                                PortName = ledDeviceSettings.SerialPortOptions.Port ?? WinSerialPortToLinuxPortName((playerIndex == 1 ? 21 : 22)),
#endif
                                BaudRate = ledDeviceSettings.SerialPortOptions.BaudRate ?? 115200,
                            };
                            break;
                        case DeviceManufacturerOption.Pipe:
                            buttonRingType = ButtonRingDeviceOption.Pipe;
                            break;
                    }
                }
                else
                {
                    var yuanDefaultHidPID = YUAN_HID_1P_PID;
                    var yuanDefaultHidVID = YUAN_HID_1P_VID;
                    var daoDefaultHidPID = DAO_HID_PID;
                    var daoDefaultHidVID = DAO_HID_VID;
                    var novDefaultUsbPID = NOV_USB_PID;
                    var novDefaultUsbVID = NOV_USB_VID;
                    var generalDefaultHidPID = GENERAL_HID_1P_PID;
                    var generalDefaultHidVID = GENERAL_HID_1P_VID;

                    var touchPanelDefaultSerialPort = 3;
                    var ledDeviceDefaultSerialPort = 21;

                    if (playerIndex != 1)
                    {
                        yuanDefaultHidPID = YUAN_HID_2P_PID;
                        yuanDefaultHidVID = YUAN_HID_2P_VID;
                        generalDefaultHidPID = GENERAL_HID_2P_PID;
                        generalDefaultHidVID = GENERAL_HID_2P_VID;
                        touchPanelDefaultSerialPort = 4;
                        ledDeviceDefaultSerialPort = 22;
                    }


                    MajDebug.LogInfo("User has not set the IO manufacturer and button ring type, will be detected automatically");
                    var filteredHidDevices = hidDevices.Where(x =>
                    {
                        var isYuan = x.ProductID == yuanDefaultHidPID && x.VendorID == yuanDefaultHidVID;
                        var isDao = x.ProductID == daoDefaultHidPID && x.VendorID == daoDefaultHidVID;
                        var isGeneral = x.ProductID == generalDefaultHidPID && x.VendorID == generalDefaultHidVID;
                        var isNov = x.ProductID == novDefaultUsbPID && x.VendorID == novDefaultUsbVID;
                        var result = isYuan || isDao || isGeneral;

                        return result;
                    });
                    if (filteredHidDevices.Count() != 0 || hasNovUsbDevice)
                    {
                        if (hidDevices.Any(x => x.ProductID == yuanDefaultHidPID && x.VendorID == yuanDefaultHidVID))
                        {
                            MajDebug.LogInfo("Manufacturer detect result: Yuan");
                            manufacturer = DeviceManufacturerOption.Yuan;
                            buttonRingType = ButtonRingDeviceOption.HID;
                            ButtonRingHidConnInfo = new()
                            {
                                DeviceName = string.Empty,
                                ProductId = yuanDefaultHidPID,
                                VendorId = yuanDefaultHidVID,
                                Exclusice = false,
                                OpenPriority = HidOpenPriority.VeryHigh
                            };
                            TouchPanelSerialConnInfo = new()
                            {
#if UNITY_STANDALONE_WIN
                                PortName = "COM" + touchPanelDefaultSerialPort,
#else
                                PortName = WinSerialPortToLinuxPortName(touchPanelDefaultSerialPort),
#endif
                                BaudRate = 9600,
                            };
                            LedDeviceSerialConnInfo = new()
                            {
#if UNITY_STANDALONE_WIN
                                PortName = "COM" + ledDeviceDefaultSerialPort,
#else
                                PortName = WinSerialPortToLinuxPortName(ledDeviceDefaultSerialPort),
#endif
                                BaudRate = 115200,
                            };
                        }
                        else if (hidDevices.Any(x => x.ProductID == daoDefaultHidPID && x.VendorID == daoDefaultHidVID))
                        {
                            MajDebug.LogInfo("Manufacturer detect result: Dao");
                            manufacturer = DeviceManufacturerOption.Dao;
                            buttonRingType = ButtonRingDeviceOption.HID;
                            ButtonRingHidConnInfo = new()
                            {
                                DeviceName = string.Empty,
                                ProductId = daoDefaultHidPID,
                                VendorId = daoDefaultHidVID,
                                Exclusice = false,
                                OpenPriority = HidOpenPriority.VeryHigh
                            };
                            LedDeviceHidConnInfo = new()
                            {
                                DeviceName = string.Empty,
                                ProductId = daoDefaultHidPID,
                                VendorId = daoDefaultHidVID,
                                Exclusice = false,
                                OpenPriority = HidOpenPriority.VeryHigh
                            };
                        }
                        else if (hasNovUsbDevice)
                        {
                            MajDebug.LogInfo("Manufacturer detect result: Nov");
                            manufacturer = DeviceManufacturerOption.Nov;
                            buttonRingType = ButtonRingDeviceOption.Keyboard;
                            TouchPanelUsbConnInfo = new()
                            {
                                DeviceName = string.Empty,
                                ProductId = novDefaultUsbPID,
                                VendorId = novDefaultUsbVID,
                                Configuration = 1,
                                Interface = 1,
                                PacketSize = 64
                            };
                            LedDeviceSerialConnInfo = new()
                            {
#if UNITY_STANDALONE_WIN
                                PortName = "COM" + ledDeviceDefaultSerialPort,
#else
                                PortName = WinSerialPortToLinuxPortName(ledDeviceDefaultSerialPort),
#endif
                                BaudRate = 115200,
                            };
                        }
                        else if (hidDevices.Any(x => x.ProductID == generalDefaultHidPID && x.VendorID == generalDefaultHidVID))
                        {
                            MajDebug.LogInfo("Manufacturer detect result: General");
                            manufacturer = DeviceManufacturerOption.General;
                            buttonRingType = ButtonRingDeviceOption.HID;
                            TouchPanelSerialConnInfo = new()
                            {
#if UNITY_STANDALONE_WIN
                                PortName = "COM" + touchPanelDefaultSerialPort,
#else
                                PortName = WinSerialPortToLinuxPortName(touchPanelDefaultSerialPort),
#endif
                                BaudRate = 9600,
                            };
                            ButtonRingHidConnInfo = new()
                            {
                                DeviceName = string.Empty,
                                ProductId = generalDefaultHidPID,
                                VendorId = generalDefaultHidVID,
                                Exclusice = false,
                                OpenPriority = HidOpenPriority.VeryHigh
                            };
                            LedDeviceSerialConnInfo = new()
                            {
#if UNITY_STANDALONE_WIN
                                PortName = "COM" + ledDeviceDefaultSerialPort,
#else
                                PortName = WinSerialPortToLinuxPortName(ledDeviceDefaultSerialPort),
#endif
                                BaudRate = 115200,
                            };
                        }
                        else
                        {
                            throw new ArgumentException("?");
                        }
                    }
                    else
                    {
                        MajDebug.LogWarning("No HID or USB device detected, fallback to keyboard");
                        manufacturer = DeviceManufacturerOption.General;
                        buttonRingType = ButtonRingDeviceOption.Keyboard;
                        TouchPanelSerialConnInfo = new()
                        {
#if UNITY_STANDALONE_WIN
                            PortName = "COM" + touchPanelDefaultSerialPort,
#else
                            PortName = WinSerialPortToLinuxPortName(touchPanelDefaultSerialPort),
#endif
                            BaudRate = 9600,
                        };
                        LedDeviceSerialConnInfo = new()
                        {
#if UNITY_STANDALONE_WIN
                            PortName = "COM" + ledDeviceDefaultSerialPort,
#else
                            PortName = WinSerialPortToLinuxPortName(ledDeviceDefaultSerialPort),
#endif
                            BaudRate = 115200,
                        };
                    }
                }
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
            }
            finally
            {
                MajDebug.LogInfo($"Player: {(playerIndex == 1 ? "1P" : "2P")}");
                MajDebug.LogInfo($"IO manufacturer: {manufacturer}");
                MajDebug.LogInfo($"Button ring device type: {buttonRingType}");
                DeviceManufacturer = manufacturer;
                ButtonRingDevice = buttonRingType;
                PlayerIndex = playerIndex;

                _isInited = true;
            }
#endif
        }
#if UNITY_STANDALONE_WIN
        static bool HasWinUsbDevice(int vid, int pid)
        {
            try
            {
                return WinUsbIo.EnumerateWinUsbInterfaces((ushort)vid, (ushort)pid).Count > 0;
            }
            catch (Exception e)
            {
                MajDebug.LogWarning($"WinUSB 设备枚举失败 (VID {vid:X4}, PID {pid:X4}): {e.Message}");
                return false;
            }
        }
#endif
        static string WinSerialPortToLinuxPortName(int port)
        {
            return $"/dev/ttyUSB{port - 1}";
        }
        public readonly struct HidConnInfo
        {
            public string DeviceName { get; init; }
            public int ProductId { get; init; }
            public int VendorId { get; init; }
            public bool Exclusice { get; init; }
            public HidOpenPriority OpenPriority { get; init; }
        }
        public readonly struct UsbConnInfo
        {
            public string DeviceName { get; init; }
            public int ProductId { get; init; }
            public int VendorId { get; init; }
            public byte Configuration { get; init; }
            public int Interface { get; init; }
            public int PacketSize { get; init; }
        }
        public readonly struct SerialPortConnInfo
        {
            public string PortName { get; init; }
            public int BaudRate { get; init; }
        }
    }
}
