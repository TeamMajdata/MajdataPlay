using MajdataPlay.Diagnostics;
using MajdataPlay.Threading;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

#nullable enable
namespace MajdataPlay.IO
{
    /// <summary>Owns a device's connection, reconnect loop and daemon lifetime.</summary>
    internal abstract class IODevice : IDisposable
    {
        public bool IsConnected
        {
            get => Volatile.Read(ref _isConnected) != 0;
            protected set => Volatile.Write(ref _isConnected, value ? 1 : 0);
        }

        protected virtual string DaemonThreadName => $"IO/{GetType().Name} Thread";
        protected virtual TimeSpan PollingInterval => TimeSpan.Zero;

        readonly object _lifetimeLock = new();
        CancellationTokenSource? _cancellationSource;
        Thread? _daemon;
        int _isConnected;
        bool _isDisposed;

        public void Start()
        {
            lock (_lifetimeLock)
            {
                if (_isDisposed)
                {
                    throw new ObjectDisposedException(GetType().Name);
                }
                if (_daemon is not null)
                {
                    return;
                }

                var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(MajEnv.GlobalCT);
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _cancellationSource = cancellationSource;
                _daemon = new Thread(() => Run(cancellationSource.Token, completion))
                {
                    Name = DaemonThreadName,
                    IsBackground = true,
                    Priority = MajEnv.THREAD_PRIORITY_IO,
                };
                completion.Task.RegisterAsWorker(DaemonThreadName);
                try
                {
                    _daemon.Start();
                }
                catch
                {
                    _daemon = null;
                    _cancellationSource = null;
                    cancellationSource.Dispose();
                    completion.TrySetResult(true);
                    throw;
                }
            }
        }

        public virtual void OnPreUpdate()
        {
        }

        protected abstract bool Connect(CancellationToken token);
        protected abstract void Update(CancellationToken token);
        protected abstract void Parse(ReadOnlySpan<byte> data);

        /// <summary>Also called during cancellation to unblock pending I/O; must be idempotent.</summary>
        protected virtual void Disconnect()
        {
        }

        protected virtual void OnDisconnected()
        {
        }

        void Run(CancellationToken token, TaskCompletionSource<bool> completion)
        {
            try
            {
                using (token.Register(DisconnectSafely))
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            if (Connect(token))
                            {
                                token.ThrowIfCancellationRequested();
                                IsConnected = true;
                                while (!token.IsCancellationRequested)
                                {
                                    var started = Stopwatch.GetTimestamp();
                                    Update(token);
                                    var interval = PollingInterval;
                                    if (interval > TimeSpan.Zero)
                                    {
                                        var elapsed = TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency);
                                        if (elapsed < interval && token.WaitHandle.WaitOne(interval - elapsed))
                                        {
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception e)
                        {
                            if (!token.IsCancellationRequested)
                            {
                                MajDebug.LogError(GetType().Name, e);
                            }
                        }
                        finally
                        {
                            IsConnected = false;
                            DisconnectSafely();
                            try
                            {
                                OnDisconnected();
                            }
                            catch (Exception e)
                            {
                                MajDebug.LogError(GetType().Name, e);
                            }
                        }

                        if (token.WaitHandle.WaitOne(MajEnv.IO_DEVICE_RECONNECT_INTERVAL_MSEC))
                        {
                            break;
                        }
                    }
                }
            }
            finally
            {
                IsConnected = false;
                lock (_lifetimeLock)
                {
                    _cancellationSource?.Dispose();
                    _cancellationSource = null;
                }
                completion.TrySetResult(true);
            }
        }

        void DisconnectSafely()
        {
            try
            {
                Disconnect();
            }
            catch (Exception e)
            {
                MajDebug.LogError(GetType().Name, e);
            }
        }

        public void Dispose()
        {
            Thread? daemon;
            lock (_lifetimeLock)
            {
                if (_isDisposed)
                {
                    return;
                }
                _isDisposed = true;
                _cancellationSource?.Cancel();
                daemon = _daemon;
            }
            if (daemon is not null && daemon != Thread.CurrentThread)
            {
                daemon.Join(MajEnv.IO_DEVICE_RECONNECT_INTERVAL_MSEC + 1000);
            }
            IsConnected = false;
        }
    }
}
