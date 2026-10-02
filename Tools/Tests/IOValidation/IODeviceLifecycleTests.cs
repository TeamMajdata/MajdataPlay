using MajdataPlay;
using MajdataPlay.IO;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

internal static class IODeviceLifecycleTests
{
    public static void Run()
    {
        StartIsIdempotentAndDisposeUnblocksIo();
        GlobalCancellationUnblocksIo();
        FailedConnectionsAndLostDevicesReconnect();
    }

    static void StartIsIdempotentAndDisposeUnblocksIo()
    {
        var device = new BlockingDevice();
        try
        {
            device.Start();
            device.Start();
            Check(device.Entered.Wait(TimeSpan.FromSeconds(5)), "The daemon did not enter its read.");
            Check(device.ConnectCount == 1, "Starting a device twice created multiple connections.");
            Check(device.IsConnected, "The opened device was not reported as connected.");
            Check(device.WorkerName == "IO/Lifecycle Test Thread", "The daemon name override was ignored.");
            Check(device.WorkerIsBackground, "The device daemon must be a background thread.");

            var stopwatch = Stopwatch.StartNew();
            device.Dispose();
            Check(stopwatch.Elapsed < TimeSpan.FromSeconds(2), "Disposal did not unblock pending I/O.");
            Check(!device.IsConnected, "A disposed device remained connected.");
            Check(device.DisconnectedCount == 1, "Disconnect notification must run once for the connection.");
            device.Dispose();
            Check(device.DisconnectedCount == 1, "Repeated disposal notified disconnection twice.");

            var startRejected = false;
            try
            {
                device.Start();
            }
            catch (ObjectDisposedException)
            {
                startRejected = true;
            }
            Check(startRejected, "Disposed devices must not create a new daemon.");
        }
        finally
        {
            device.Dispose();
        }
    }

    static void FailedConnectionsAndLostDevicesReconnect()
    {
        using var device = new ReconnectingDevice();
        device.Start();
        Check(device.Reconnected.Wait(TimeSpan.FromSeconds(8)), "The failed device was not reconnected.");
        Check(device.ConnectCount == 3, "An unavailable device and a lost connection must each be retried.");
        Check(device.DisconnectedCount == 2, "Failed attempts did not clear their device state.");
        Check(device.IsConnected, "The reconnected device was not reported as connected.");
    }

    static void GlobalCancellationUnblocksIo()
    {
        var originalToken = MajEnv.GlobalCT;
        using var cancellationSource = new CancellationTokenSource();
        var device = new BlockingDevice();
        try
        {
            MajEnv.GlobalCT = cancellationSource.Token;
            device.Start();
            Check(device.Entered.Wait(TimeSpan.FromSeconds(5)), "The globally cancellable daemon did not start.");
            cancellationSource.Cancel();
            Check(SpinWait.SpinUntil(() => Volatile.Read(ref device.DisconnectedCount) == 1, TimeSpan.FromSeconds(5)),
                "Global cancellation did not unblock the device and notify disconnection.");
            Check(!device.IsConnected, "Global cancellation left the device connected.");
        }
        finally
        {
            device.Dispose();
            MajEnv.GlobalCT = originalToken;
        }
    }

    static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    sealed class BlockingDevice : IODevice
    {
        public readonly ManualResetEventSlim Entered = new();
        readonly ManualResetEventSlim _unblock = new();
        public int ConnectCount;
        public int DisconnectedCount;
        public string? WorkerName;
        public bool WorkerIsBackground;

        protected override string DaemonThreadName => "IO/Lifecycle Test Thread";

        protected override bool Connect(CancellationToken token)
        {
            Interlocked.Increment(ref ConnectCount);
            return true;
        }

        protected override void Update(CancellationToken token)
        {
            WorkerName = Thread.CurrentThread.Name;
            WorkerIsBackground = Thread.CurrentThread.IsBackground;
            Entered.Set();
            // Models synchronous I/O which only returns when its connection is closed.
            _unblock.Wait();
            token.ThrowIfCancellationRequested();
        }

        protected override void Disconnect() => _unblock.Set();
        protected override void OnDisconnected() => Interlocked.Increment(ref DisconnectedCount);
        protected override void Parse(ReadOnlySpan<byte> data) { }
    }

    sealed class ReconnectingDevice : IODevice
    {
        public readonly ManualResetEventSlim Reconnected = new();
        readonly ManualResetEventSlim _unblock = new();
        public int ConnectCount;
        public int DisconnectedCount;

        protected override bool Connect(CancellationToken token)
        {
            _unblock.Reset();
            return Interlocked.Increment(ref ConnectCount) > 1;
        }

        protected override void Update(CancellationToken token)
        {
            if (ConnectCount == 2)
            {
                throw new IOException("Simulated unplugged device.");
            }
            Reconnected.Set();
            _unblock.Wait();
            token.ThrowIfCancellationRequested();
        }

        protected override void Disconnect() => _unblock.Set();
        protected override void OnDisconnected() => Interlocked.Increment(ref DisconnectedCount);
        protected override void Parse(ReadOnlySpan<byte> data) { }
    }
}
