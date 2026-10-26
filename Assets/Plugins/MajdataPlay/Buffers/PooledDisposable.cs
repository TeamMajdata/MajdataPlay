using System;
using System.Threading;

#nullable enable
namespace MajdataPlay.Buffers
{
    /// <summary>
    /// Shares a disposal state between value-type copies using a pooled, versioned state object.
    /// </summary>
    /// <remarks>
    /// Obtain a live instance with <see cref="Rent"/> and copy it into each participating struct.
    /// Disposing any copy permanently disposes every copy of that lease, even after its state object
    /// is reused. A default instance is already disposed. This is not a reference count: disposing
    /// one copy ends the shared lifetime without waiting for the other copies.
    /// All operations on this token are thread-safe, but they do not synchronize access to an owner's
    /// resources. Disposal marks the lifetime as ended before any owner cleanup has completed.
    /// </remarks>
    /// <example>
    /// Store a rented token in an owner's field and copy that token when sharing the lifetime:
    /// <code>
    /// var lifetime = PooledDisposable.Rent();
    /// var copy = lifetime;
    /// if (copy.TryDispose())
    /// {
    ///     // Release the owner's resources exactly once here.
    /// }
    /// lifetime.ThrowIfDisposed(); // Throws even if the pooled state has already been reused.
    /// </code>
    /// </example>
    public readonly struct PooledDisposable : IDisposable
    {
        /// <summary>
        /// The maximum number of idle state objects retained by the shared pool.
        /// </summary>
        private const int PoolCapacity = 8192;

        /// <summary>
        /// Protects the shared pool's slots and count.
        /// </summary>
        private static readonly object s_poolLock = new();

        /// <summary>
        /// Stores idle states without allocating collection nodes on return.
        /// </summary>
        private static readonly State?[] s_pool = new State?[PoolCapacity];

        /// <summary>
        /// The number of occupied pool slots, accessed only while holding the pool lock.
        /// </summary>
        private static int s_poolCount;

        /// <summary>
        /// The shared state object, or null for a default, already disposed instance.
        /// </summary>
        private readonly State? _state;

        /// <summary>
        /// The positive version identifying this lease of the shared state object.
        /// </summary>
        private readonly long _version;

        /// <summary>
        /// Gets whether this lease is disposed or was never rented.
        /// </summary>
        /// <remarks>
        /// This is a snapshot, not a lock on an owner's resources or a guarantee that cleanup is complete.
        /// </remarks>
        public bool IsDisposed
        {
            get
            {
                return _state is null || Volatile.Read(ref _state._version) != _version;
            }
        }

        /// <summary>
        /// Creates a token for one live lease of a shared state object.
        /// </summary>
        /// <param name="state">The state object exclusively acquired for this lease.</param>
        /// <param name="version">The positive version assigned to this lease.</param>
        private PooledDisposable(State state, long version)
        {
            _state = state;
            _version = version;
        }

        /// <summary>
        /// Rents a new, independent disposal lifetime, reusing an idle state object when available.
        /// </summary>
        /// <returns>A live token whose copies share the same disposal state.</returns>
        /// <remarks>
        /// Dispose at least one copy to return its state object to the bounded shared pool.
        /// Renting allocates a state object only when the pool is empty.
        /// </remarks>
        public static PooledDisposable Rent()
        {
            var state = RentState();
            // A new state is zero; an idle state contains the negated version of its previous lease.
            var version = 1 - Volatile.Read(ref state._version);
            Volatile.Write(ref state._version, version);
            return new PooledDisposable(state, version);
        }

        /// <summary>
        /// Atomically ends this lease and returns its state object to the pool when possible.
        /// </summary>
        /// <returns>
        /// True only for the copy that first ends this lease; false for default, disposed or stale copies.
        /// </returns>
        /// <remarks>
        /// Owners should perform their resource cleanup only when this method returns true.
        /// This method does not perform that cleanup or wait for another copy's cleanup to complete.
        /// </remarks>
        public bool TryDispose()
        {
            var state = _state;
            if (state is null || Interlocked.CompareExchange(ref state._version, -_version, _version) != _version)
            {
                return false;
            }

            // Retire exhausted states instead of wrapping the version and reviving an old lease.
            if (_version != long.MaxValue)
            {
                ReturnState(state);
            }
            return true;
        }

        /// <summary>
        /// Ends this shared lifetime; repeated calls and calls on default instances have no effect.
        /// </summary>
        public void Dispose()
        {
            TryDispose();
        }

        /// <summary>
        /// Throws if this lease is disposed or was never rented.
        /// </summary>
        /// <param name="objectName">
        /// The owner's name to include in the exception, or null to use this token's type name.
        /// </param>
        /// <exception cref="ObjectDisposedException">This lease is disposed or was never rented.</exception>
        public void ThrowIfDisposed(string? objectName = null)
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(objectName ?? nameof(PooledDisposable));
            }
        }

        /// <summary>
        /// Takes exclusive ownership of an idle state, or allocates a new state if the pool is empty.
        /// </summary>
        /// <returns>A state that has not yet been activated for its next lease.</returns>
        private static State RentState()
        {
            lock (s_poolLock)
            {
                if (s_poolCount > 0)
                {
                    s_poolCount--;
                    var state = s_pool[s_poolCount]!;
                    s_pool[s_poolCount] = null;
                    return state;
                }
            }
            return new State();
        }

        /// <summary>
        /// Retains a disposed state for reuse if the bounded pool has room.
        /// </summary>
        /// <param name="state">The state returned by the sole successful disposer of its current lease.</param>
        private static void ReturnState(State state)
        {
            lock (s_poolLock)
            {
                if (s_poolCount < s_pool.Length)
                {
                    s_pool[s_poolCount] = state;
                    s_poolCount++;
                }
            }
        }

        /// <summary>
        /// Holds the atomic version shared by all copies of a token.
        /// </summary>
        private sealed class State
        {
            /// <summary>
            /// Zero before first use, a positive version while live, or its negative after disposal.
            /// </summary>
            internal long _version;
        }
    }
}
