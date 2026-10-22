using System;
using System.Threading;
using MajdataPlay.IO;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>
    /// An <see cref="IODevice"/> whose connection, update and disconnect behaviour is scripted by the test, so the
    /// daemon thread's reconnect loop can be exercised without hardware.
    /// </summary>
    internal sealed class FakeIODevice : IODevice
    {
        /// <summary>The number of connection attempts.</summary>
        private int _connectCount;

        /// <summary>The number of update calls.</summary>
        private int _updateCount;

        /// <summary>The number of disconnect calls.</summary>
        private int _disconnectCount;

        /// <summary>The number of disconnection notifications.</summary>
        private int _disconnectedCount;

        /// <summary>Gets or sets the behaviour of a connection attempt; it returns whether the device connected.</summary>
        public Func<CancellationToken, bool> ConnectBehaviour { get; set; } = static _ => true;

        /// <summary>Gets or sets the behaviour of one update; by default it blocks until cancellation.</summary>
        public Action<CancellationToken> UpdateBehaviour { get; set; } = static token => token.WaitHandle.WaitOne();

        /// <summary>Gets or sets a value indicating whether <see cref="Disconnect"/> throws.</summary>
        public bool ThrowOnDisconnect { get; set; }

        /// <summary>Gets or sets a value indicating whether <see cref="OnDisconnected"/> throws.</summary>
        public bool ThrowOnDisconnected { get; set; }

        /// <summary>Gets or sets the polling interval reported to the daemon.</summary>
        public TimeSpan Interval { get; set; } = TimeSpan.Zero;

        /// <summary>Gets the number of connection attempts.</summary>
        public int ConnectCount => Volatile.Read(ref _connectCount);

        /// <summary>Gets the number of update calls.</summary>
        public int UpdateCount => Volatile.Read(ref _updateCount);

        /// <summary>Gets the number of disconnect calls.</summary>
        public int DisconnectCount => Volatile.Read(ref _disconnectCount);

        /// <summary>Gets the number of disconnection notifications.</summary>
        public int DisconnectedCount => Volatile.Read(ref _disconnectedCount);

        /// <summary>Gets the name of the daemon thread.</summary>
        public string ThreadName => DaemonThreadName;

        /// <inheritdoc/>
        protected override TimeSpan PollingInterval => Interval;

        /// <inheritdoc/>
        protected override bool Connect(CancellationToken token)
        {
            Interlocked.Increment(ref _connectCount);
            return ConnectBehaviour(token);
        }

        /// <inheritdoc/>
        protected override void Update(CancellationToken token)
        {
            Interlocked.Increment(ref _updateCount);
            UpdateBehaviour(token);
        }

        /// <inheritdoc/>
        protected override void Parse(ReadOnlySpan<byte> data)
        {
        }

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException"><see cref="ThrowOnDisconnect"/> is set.</exception>
        protected override void Disconnect()
        {
            Interlocked.Increment(ref _disconnectCount);
            if (ThrowOnDisconnect)
            {
                throw new InvalidOperationException("disconnect failed");
            }
        }

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException"><see cref="ThrowOnDisconnected"/> is set.</exception>
        protected override void OnDisconnected()
        {
            Interlocked.Increment(ref _disconnectedCount);
            if (ThrowOnDisconnected)
            {
                throw new InvalidOperationException("notification failed");
            }
        }
    }
}
