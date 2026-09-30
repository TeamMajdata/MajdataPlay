#if UNITY_STANDALONE_WIN
using System;
using System.Threading;
using MajdataPlay.Diagnostics;

namespace MajdataPlay.IO
{
    /// <summary>
    /// 独占触摸设备槽：按顺序尝试各设备工厂，未连接时后台轮询重试。
    /// 已连接的设备由自身的读线程负责断线重连。
    /// </summary>
    internal static class ExclusiveTouchHost
    {
        private static ExclusiveTouchBase? _device;
        private static volatile bool _quitting;

        public static bool IsConnected => Volatile.Read(ref _device)?.IsConnected ?? false;

        public static ulong GetTouchState() => Volatile.Read(ref _device)?.GetTouchState() ?? 0;

        public static void WaitForData(int millisecondsTimeout)
        {
            var device = Volatile.Read(ref _device);
            if (device == null)
            {
                Thread.Sleep(millisecondsTimeout);
                return;
            }
            device.WaitForData(millisecondsTimeout);
        }

        public static void Start(params Func<ExclusiveTouchBase>[] factories)
        {
            _quitting = false;
            MajEnv.GlobalCT.Register(() => _quitting = true);

            var device = StartDevice(factories, logConnectionFailure: true);
            if (device != null)
            {
                Volatile.Write(ref _device, device);
                return;
            }

            MajDebug.LogInfo("[ExclusiveTouch] 等待设备连接");
            var retryThread = new Thread(() => Retry(factories))
            {
                IsBackground = true,
                Name = "IO/ExclusiveTouch Retry Thread",
                Priority = MajEnv.THREAD_PRIORITY_IO
            };
            retryThread.Start();
        }

        public static void Stop()
        {
            _quitting = true;
            Volatile.Read(ref _device)?.Stop();
            Volatile.Write(ref _device, null);
        }

        private static void Retry(Func<ExclusiveTouchBase>[] factories)
        {
            while (!_quitting)
            {
                Thread.Sleep(1000);
                if (_quitting) return;

                var device = StartDevice(factories, logConnectionFailure: false);
                if (device == null) continue;

                Volatile.Write(ref _device, device);
                MajDebug.LogInfo("[ExclusiveTouch] 设备已连接");
                return;
            }
        }

        private static ExclusiveTouchBase? StartDevice(Func<ExclusiveTouchBase>[] factories, bool logConnectionFailure)
        {
            foreach (var factory in factories)
            {
                var device = factory();
                if (device.Start(logConnectionFailure)) return device;
            }

            return null;
        }
    }
}
#endif
