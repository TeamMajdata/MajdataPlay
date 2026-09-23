using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Settings;
using MajdataPlay.Numerics;
using UnityEngine;
using MajdataPlay.Diagnostics;
using MajdataPlay.Runtime;
using System.IO.Pipes;





#if UNITY_STANDALONE
using HidSharp;
#nullable enable
namespace MajdataPlay.IO
{
    public static partial class OutputManager
    {
        static class LedDevice
        {
            const string DAEMON_THREAD_NAME = "IO/Led Thread";
            public static bool IsConnected
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get;
                private set;
            }

            static int _isInited = 0;
            static bool _isEnabled = true;
            static float _brightness = 1.0f;
            static bool _isThrottlerEnabled = false;
            static int _refreshRateMs = 16;

            static Task _ledDeviceUpdateLoop = Task.CompletedTask;

            public static void Init()
            {
                if (Interlocked.CompareExchange(ref _isInited, 1, 0) != 0)
                {
                    return;
                }
                MajDebug.LogInfo(nameof(LedDevice), "Start initialization");
                _isEnabled = MajEnv.Settings.IO.OutputDevice.Led.Enable;
                _isThrottlerEnabled = MajEnv.Settings.IO.OutputDevice.Led.Throttler;
                _refreshRateMs = MajEnv.Settings.IO.OutputDevice.Led.RefreshRateMs;
                _brightness = MajEnv.Settings.IO.OutputDevice.Led.Brightness.Clamp(0, 1f);
                if (!_isEnabled)
                {
                    MajDebug.LogInfo(nameof(LedDevice), "Disabled");
                    return;
                }
                else if (!_ledDeviceUpdateLoop.IsCompleted)
                {
                    return;
                }
                try
                {
                    var manufacturer = IODetector.DeviceManufacturer;
                    if (manufacturer is DeviceManufacturerOption.Yuan)
                    {
                        _refreshRateMs = Mathf.Max(_refreshRateMs, 100);
                    }
                    switch (manufacturer)
                    {
                        case DeviceManufacturerOption.General:
                        case DeviceManufacturerOption.Yuan:
                        case DeviceManufacturerOption.Nov:
                            _ledDeviceUpdateLoop = Task.Factory.StartNew(SerialPortUpdateLoop, TaskCreationOptions.LongRunning);
                            break;
                        case DeviceManufacturerOption.Dao:
                            _ledDeviceUpdateLoop = Task.Factory.StartNew(HIDUpdateLoop, TaskCreationOptions.LongRunning);
                            break;
                        case DeviceManufacturerOption.Pipe:
                            _ledDeviceUpdateLoop = Task.Factory.StartNew(PipeUpdateLoop, TaskCreationOptions.LongRunning);
                            break;
                        default:
                            MajDebug.LogWarning(nameof(LedDevice), $"Not supported led device manufacturer: {manufacturer}");
                            break;
                    }
                }
                catch
                {
                    //MajDebug.LogWarning($"Cannot open {comPortStr}, using dummy lights");
                    IsConnected = false;
                }
                MajDebug.LogInfo(nameof(LedDevice), "Initialization completed");
            }
            static void SerialPortUpdateLoop()
            {
                var currentThread = Thread.CurrentThread;
                var serialPortOptions = IODetector.LedDeviceSerialConnInfo;
                var token = MajEnv.GlobalCT;
                var comPort = serialPortOptions.PortName;
                var refreshRate = TimeSpan.FromMilliseconds(MajEnv.Settings.IO.OutputDevice.Led.RefreshRateMs);
                var stopwatch = new Stopwatch();
                var t1 = stopwatch.Elapsed;
                var ledRingColors = _ledRingColors.AsSpan();
                var updatePacket = GeneralSerialLedDevice.BuildUpdatePacket();

                currentThread.Name = DAEMON_THREAD_NAME;
                currentThread.IsBackground = true;
                currentThread.Priority = MajEnv.THREAD_PRIORITY_IO;

                MajDebug.LogInfo(nameof(LedDevice), $"Managed thread id: {currentThread.ManagedThreadId}");
                MajDebug.LogInfo(nameof(LedDevice), $"OS thread id: {PlatformInfo.GetCurrentOSThreadId()}");

                Span<byte> buffer = stackalloc byte[10];
                Span<LedReport> latestReports = stackalloc LedReport[8]
                {
                    new LedReport()
                    {
                        Index = 0,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 1,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 2,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 3,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 4,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 5,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 6,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 7,
                        Color = Color.black,
                    }
                };

                stopwatch.Start();
                var serialDevice = default(SerialDevice?);
                var serialStream = default(SerialStream?);
                var isReconnecting = false;

                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        Thread.Sleep(MajEnv.IO_DEVICE_RECONNECT_INTERVAL_MSEC); // Reconnect interval
                        serialDevice = DeviceList.Local.GetSerialDeviceOrNull(comPort);
                        serialStream = default(SerialStream?);
                        if (serialDevice is null)
                        {
                            if (isReconnecting)
                            {
                                MajDebug.LogWarning(nameof(LedDevice), $"{comPort} was lost, waiting for serial device to reconnect");
                                continue;
                            }
                            else
                            {
                                MajDebug.LogWarning(nameof(LedDevice), $"{comPort} not found, using dummy led device as fallback");
                                return;
                            }
                        }
                        else
                        {
                            MajDebug.LogInfo(nameof(LedDevice), $"Trying to open serial port \"{comPort}\" with {serialPortOptions.BaudRate} baud rate...");
                            if (serialDevice.TryOpen(out serialStream))
                            {
                                MajDebug.LogInfo(nameof(LedDevice), $"\"{comPort}\" is opened");
                                serialStream.BaudRate = serialPortOptions.BaudRate;
                                serialStream.DataBits = 8;
                                serialStream.Parity = SerialParity.None;
                                serialStream.StopBits = 1;
                                serialStream.DtrEnable = true;
                                serialStream.RtsEnable = true;
                                serialStream.ReadTimeout = 2000;
                                serialStream.WriteTimeout = 2000;

                                MajDebug.LogInfo(nameof(LedDevice), "Connected");
                            }
                            else if (isReconnecting)
                            {
                                MajDebug.LogError(nameof(LedDevice), $"Cannot open {comPort}");
                                continue;
                            }
                            else
                            {
                                MajDebug.LogError(nameof(LedDevice), $"Cannot open {comPort}, using dummy led device as fallback");
                                return;
                            }
                        }
                        #region Polling
                        IsConnected = true;
                        isReconnecting = true;
                        while (!token.IsCancellationRequested)
                        {
                            try
                            {
                                var needUpdate = false;
                                for (var i = 0; i < 8; i++)
                                {
                                    var color = ledRingColors[i];
                                    ref var latestReport = ref latestReports[i];
                                    if (latestReport.Color == color && _isThrottlerEnabled)
                                    {
                                        continue;
                                    }
                                    var packet = GeneralSerialLedDevice.BuildSetColorPacket(buffer, i, color);
                                    latestReport = new()
                                    {
                                        Index = i,
                                        Color = color,
                                    };
                                    needUpdate = true;
                                    serialStream.Write(packet);
                                }
                                if (needUpdate)
                                {
                                    serialStream.Write(updatePacket);
                                }
                            }
                            catch (IOException e)
                            {
                                IsConnected = false;
                                MajDebug.LogError(nameof(LedDevice), e);
                                serialStream?.Close();
                                serialStream?.Dispose();
                                serialDevice = default;
                                MajDebug.LogInfo(nameof(LedDevice), $"Disconnected");
                                break;
                            }
                            catch (Exception e)
                            {
                                MajDebug.LogError(nameof(LedDevice), $"\n{e}");
                            }
                            finally
                            {
                                var t2 = stopwatch.Elapsed;
                                var elapsed = t2 - t1;
                                t1 = t2;
                                if (elapsed < refreshRate)
                                {
                                    Thread.Sleep(refreshRate - elapsed);
                                }
                            }
                        }
                        #endregion
                    }
                }
                finally
                {
                    IsConnected = false;
                    serialStream?.Close();
                    serialStream?.Dispose();
                    MajDebug.LogWarning(nameof(LedDevice), "Thread has exited");
                }
            }
            static void HIDUpdateLoop()
            {
                var ledOptions = MajEnv.Settings.IO.OutputDevice.Led;
                var hidOptions = IODetector.LedDeviceHidConnInfo;
                var currentThread = Thread.CurrentThread;
                var token = MajEnv.GlobalCT;
                var refreshRate = TimeSpan.FromMilliseconds(ledOptions.RefreshRateMs);
                var stopwatch = new Stopwatch();
                var ledRingColors = _ledRingColors;
                var t1 = stopwatch.Elapsed;
                var pid = hidOptions.ProductId;
                var vid = hidOptions.VendorId;
                var deviceName = string.IsNullOrEmpty(hidOptions.DeviceName) ? string.Empty : hidOptions.DeviceName;
                var hidConfig = new OpenConfiguration();
                var filter = new DeviceFilter()
                {
                    DeviceName = deviceName,
                    ProductId = pid,
                    VendorId = vid,
                };
                Span<LedReport> latestReports = stackalloc LedReport[8]
                {
                    new LedReport()
                    {
                        Index = 0,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 1,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 2,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 3,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 4,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 5,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 6,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 7,
                        Color = Color.black,
                    }
                };

                hidConfig.SetOption(OpenOption.Exclusive, hidOptions.Exclusice);
                hidConfig.SetOption(OpenOption.Priority, (OpenPriority)hidOptions.OpenPriority);
                currentThread.Name = DAEMON_THREAD_NAME;
                currentThread.IsBackground = true;
                currentThread.Priority = MajEnv.THREAD_PRIORITY_IO;

                MajDebug.LogInfo(nameof(LedDevice), $"Managed thread id: {currentThread.ManagedThreadId}");
                MajDebug.LogInfo(nameof(LedDevice), $"OS thread id: {PlatformInfo.GetCurrentOSThreadId()}");

                var hidDevice = default(HidDevice?);
                var hidStream = default(HidStream?);
                var isReconnecting = false;
                var buffer = Span<byte>.Empty;
                stopwatch.Start();
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        Thread.Sleep(MajEnv.IO_DEVICE_RECONNECT_INTERVAL_MSEC);
                        hidDevice = default;
                        hidStream = default;
                        if (!HidHelper.TryGetAndOpenDevice(nameof(LedDevice),
                            filter, hidConfig, out hidDevice, out hidStream, isReconnecting))
                        {
                            continue;
                        }
                        MajDebug.LogInfo(nameof(LedDevice), $"Opened\nDevice: {hidDevice}");
                        // HID device has been opened
                        var outputReportId = hidDevice.GetReportDescriptor()
                                               .OutputReports
                                               .FirstOrDefault()
                                               ?.ReportID ?? 0;
                        var latestCabinetLightBrightness = byte.MaxValue;
                        var deviceReportBufferLength = hidDevice.GetMaxInputReportLength();
                        if (deviceReportBufferLength > buffer.Length)
                        {
                            buffer = new byte[deviceReportBufferLength];
                        }
                        else
                        {
                            if (buffer.IsEmpty)
                            {
                                buffer = new byte[Math.Min(4096, deviceReportBufferLength)];
                            }
                        }
                        buffer[0] = outputReportId;
                        IsConnected = true;
                        isReconnecting = true;
                        MajDebug.LogInfo(nameof(LedDevice), $"Connected\nDevice: {hidDevice}");
                        t1 = stopwatch.Elapsed;
                        #region Polling
                        while (!token.IsCancellationRequested)
                        {
                            try
                            {
                                var needUpdate = false;
                                for (var i = 0; i < 8; i++)
                                {
                                    var color = ledRingColors[i];
                                    ref var latestReport = ref latestReports[i];
                                    if (latestReport.Color == color && _isThrottlerEnabled)
                                    {
                                        continue;
                                    }
                                    latestReport = new()
                                    {
                                        Index = i,
                                        Color = color,
                                    };
                                    needUpdate = true;
                                }
                                var cabinetLightBrightness = _cabinetLightBrightness;
                                if (latestCabinetLightBrightness != cabinetLightBrightness)
                                {
                                    latestCabinetLightBrightness = cabinetLightBrightness;
                                    needUpdate = true;
                                }
                                if (needUpdate)
                                {
                                    var reportBuffer = DaoHIDLedDevice.BuildUpdatePacket(buffer, ledRingColors, cabinetLightBrightness);
                                    hidStream.Write(reportBuffer);
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                            catch (IOException ioE)
                            {
                                IsConnected = false;
                                MajDebug.LogError(nameof(LedDevice), $"\n{ioE}");
                                hidStream.Close();
                                hidStream.Dispose();
                                MajDebug.LogInfo(nameof(LedDevice), $"Disconnected");
                                break;
                            }
                            catch (Exception e)
                            {
                                MajDebug.LogError(nameof(LedDevice), $"\n{e}");
                            }
                            finally
                            {
                                buffer.Clear();
                                buffer[0] = outputReportId;
                                if (refreshRate.TotalMilliseconds > 0)
                                {
                                    var t2 = stopwatch.Elapsed;
                                    var elapsed = t2 - t1;
                                    t1 = t2;
                                    if (elapsed < refreshRate)
                                    {
                                        Thread.Sleep(refreshRate - elapsed);
                                    }
                                }
                            }
                        }
                        #endregion
                    }
                }
                finally
                {
                    IsConnected = false;
                    hidStream?.Close();
                    hidStream?.Dispose();
                    MajDebug.LogWarning(nameof(LedDevice), "Thread has exited");

                }
            }

            static void PipeUpdateLoop()
            {
                /// Payload structure (32bit)
                ///                  
                ///    b        g        r       Type
                /// 00000000 00000000 00000000 00000000
                /// 
                var ledOptions = MajEnv.Settings.IO.OutputDevice.Led;
                var pipeName = $"MajdataPlay.IO.Led.{IODetector.PlayerIndex}P";
                var token = MajEnv.GlobalCT;
                var stopwatch = new Stopwatch();
                var t1 = stopwatch.Elapsed;
                var ledRingColors = _ledRingColors.AsSpan();
                var refreshRate = TimeSpan.FromMilliseconds(ledOptions.RefreshRateMs);
                var currentThread = Thread.CurrentThread;

                if (refreshRate.TotalMilliseconds > 500)
                {
                    refreshRate = TimeSpan.FromMilliseconds(500);
                }

                currentThread.Name = DAEMON_THREAD_NAME;
                currentThread.IsBackground = true;
                currentThread.Priority = MajEnv.THREAD_PRIORITY_IO;

                MajDebug.LogInfo(nameof(LedDevice), $"Managed thread id: {currentThread.ManagedThreadId}");
                MajDebug.LogInfo(nameof(LedDevice), $"OS thread id: {PlatformInfo.GetCurrentOSThreadId()}");

                var heartBeatPacket = new PipePacket()
                {
                    Type = PipePacketType.HeartBeat,
                };
                var heartBeatData = (stackalloc byte[PipePacket.PACKET_HEADER_LENGTH * sizeof(byte)]);
                var packetBuffer = (stackalloc byte[1024]);
                var payload = (stackalloc byte[1024 - PipePacket.PACKET_HEADER_LENGTH]);
                var latestReports = (stackalloc LedReport[8]
                {
                    new LedReport()
                    {
                        Index = 0,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 1,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 2,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 3,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 4,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 5,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 6,
                        Color = Color.black,
                    },
                    new LedReport()
                    {
                        Index = 7,
                        Color = Color.black,
                    }
                });
                heartBeatPacket.Write(heartBeatData);

                while (!token.IsCancellationRequested)
                {
                    if (token.WaitHandle.WaitOne(MajEnv.IO_DEVICE_RECONNECT_INTERVAL_MSEC))
                    {
                        break;
                    }
                    using var pipeClientStream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    using var cancellation = token.Register(() => pipeClientStream.Dispose());
                    try
                    {
                        try
                        {
                            MajDebug.LogInfo(nameof(LedDevice), $"Attempting connect to pipe \"{pipeName}\"...");
                            pipeClientStream.Connect(2000);
                            MajDebug.LogInfo(nameof(LedDevice), "Connected");
                        }
                        catch (Exception) when (token.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception e)
                        {
                            MajDebug.LogError(nameof(LedDevice), $"Failed to connect to pipe\n{e}");
                            continue;
                        }
                        IsConnected = true;
                        var forceUpdate = true;
                        stopwatch.Restart();
                        while (!token.IsCancellationRequested)
                        {
                            t1 = stopwatch.Elapsed;
                            try
                            {
                                var needUpdate = false;
                                var writtenBytes = 0;
                                for (var i = 0; i < 8; i++)
                                {
                                    var color = ledRingColors[i];
                                    ref var latestReport = ref latestReports[i];
                                    if (!forceUpdate && latestReport.Color == color && _isThrottlerEnabled)
                                    {
                                        continue;
                                    }
                                    latestReport = new()
                                    {
                                        Index = i,
                                        Color = color,
                                    };
                                    needUpdate = true;
                                    payload[writtenBytes++] = (byte)i;
                                    payload[writtenBytes++] = (byte)(color.r * byte.MaxValue);
                                    payload[writtenBytes++] = (byte)(color.g * byte.MaxValue);
                                    payload[writtenBytes++] = (byte)(color.b * byte.MaxValue);
                                }
                                var packet = new PipePacket()
                                {
                                    Type = PipePacketType.Report,
                                    Length = (ushort)writtenBytes,
                                    Payload = payload.Slice(0, writtenBytes)
                                };
                                var packetLen = packet.Write(packetBuffer);
                                if (needUpdate)
                                {
                                    pipeClientStream.Write(packetBuffer.Slice(0, packetLen));
                                    forceUpdate = false;
                                }
                                else
                                {
                                    pipeClientStream.Write(heartBeatData);
                                }
                            }
                            catch (Exception) when (token.IsCancellationRequested)
                            {
                                break;
                            }
                            catch (IOException ioE)
                            {
                                IsConnected = false;
                                MajDebug.LogError(nameof(LedDevice), $"{ioE}");
                                break;
                            }
                            catch (Exception e)
                            {
                                MajDebug.LogError(nameof(LedDevice), $"{e}");
                                break;
                            }
                            finally
                            {
                                if (refreshRate.TotalMilliseconds > 0)
                                {
                                    var t2 = stopwatch.Elapsed;
                                    var elapsed = t2 - t1;
                                    t1 = t2;
                                    if (elapsed < refreshRate)
                                    {
                                        Thread.Sleep(refreshRate - elapsed);
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        IsConnected = false;
                        pipeClientStream.Dispose();
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static bool EnsureSerialPortIsOpen(SerialPort serialSession)
            {
                try
                {
                    if (serialSession.IsOpen)
                    {
                        return true;
                    }
                    else
                    {
                        serialSession.Open();
                        return true;
                    }
                }
                catch (Exception e)
                {
                    MajDebug.LogException(e);
                    return false;
                }

            }
            static class GeneralSerialLedDevice
            {
                readonly static ReadOnlyMemory<byte> _templateSingle = new byte[]
                {
                0xE0, 0x11, 0x01, 0x05, 0x31, 0x01, 0x00, 0x00, 0x00, 0x00
                };
                readonly static ReadOnlyMemory<byte> _templateUpdate = new byte[]
                {
                0xE0, 0x11, 0x01, 0x01, 0x3C, 0x4F
                };
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static ReadOnlySpan<byte> BuildSetColorPacket(Span<byte> packet, int index, Color newColor)
                {
                    _templateSingle.Span.CopyTo(packet);
                    packet[5] = (byte)index;
                    packet[6] = (byte)(newColor.r * 255 * _brightness);
                    packet[7] = (byte)(newColor.g * 255 * _brightness);
                    packet[8] = (byte)(newColor.b * 255 * _brightness);
                    packet[9] = CalculateCheckSum(packet.Slice(0, 9));

                    return packet.Slice(0, 10);
                }
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static ReadOnlySpan<byte> BuildUpdatePacket()
                {
                    return _templateUpdate.Span;
                }
                static byte CalculateCheckSum(Span<byte> bytes)
                {
                    byte sum = 0;
                    for (int i = 1; i < bytes.Length; i++)
                    {
                        sum += bytes[i];
                    }
                    return sum;
                }
            }
            static class DaoHIDLedDevice
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static ReadOnlySpan<byte> BuildUpdatePacket(Span<byte> rawBuffer, ReadOnlySpan<Color> ledColors, byte cabinetLightBrightness)
                {
                    var buffer = rawBuffer.Slice(1);
                    for (int i = 0, li = 0; li < ledColors.Length;)
                    {
                        var color = ledColors[li++];
                        var r = (byte)(color.r * 255 * _brightness);
                        var g = (byte)(color.g * 255 * _brightness);
                        var b = (byte)(color.b * 255 * _brightness);

                        buffer[i++] = r;
                        buffer[i++] = g;
                        buffer[i++] = b;
                    }
                    buffer[24] = (byte)(cabinetLightBrightness * _brightness);
                    return rawBuffer;
                }
            }
            readonly struct LedReport
            {
                public int Index { get; init; }
                public Color Color { get; init; }
            }
        }
    }
}
#endif
