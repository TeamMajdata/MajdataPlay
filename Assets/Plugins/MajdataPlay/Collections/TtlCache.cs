using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Collections
{
    /// <summary>
    /// A thread-safe cache whose values expire a fixed time after they were stored, with loading that runs once per key
    /// at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="GetOrAddAsync"/> returns a live value or loads one with the given factory. Callers that ask for the same
    /// key while it loads wait for that load and then reuse its value instead of starting their own. A factory that
    /// throws stores nothing: the exception reaches its caller and the next caller loads again.
    /// </para>
    /// <para>
    /// Removing a key, by <see cref="Remove(TKey)"/>, <see cref="RemoveWhere(Func{TKey, bool})"/> or <see cref="Clear"/>,
    /// wins over a load that is still running for it: that load's value is returned to its caller but not cached, so a
    /// value read before a change on the server is never stored after the change invalidated it.
    /// </para>
    /// <para>
    /// Expiry is measured on a monotonic clock, so changes of the system time do not shorten or extend it. Expired
    /// entries are dropped from time to time as new keys are added, or at once by <see cref="RemoveExpired"/>.
    /// </para>
    /// </remarks>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The cached value type.</typeparam>
    public sealed class TtlCache<TKey, TValue> where TKey : notnull
    {
        /// <summary>The number of stored entries above which adding a key first drops the expired entries.</summary>
        private const int MinPruneThreshold = 64;

        /// <summary>The monotonic clock of every cache that is not given its own.</summary>
        private static readonly Stopwatch s_monotonicClock = Stopwatch.StartNew();

        /// <summary>Gets how long a stored value stays live.</summary>
        public TimeSpan TimeToLive { get; }

        /// <summary>Gets the number of keys with a live value.</summary>
        public int Count
        {
            get
            {
                lock (_gate)
                {
                    var now = _clock();
                    var count = 0;
                    foreach (var entry in _entries.Values)
                    {
                        if (entry.IsLive(now))
                        {
                            count++;
                        }
                    }
                    return count;
                }
            }
        }

        /// <summary>Gets the number of stored entries, including expired ones and keys still loading; for tests.</summary>
        internal int StoredCount
        {
            get
            {
                lock (_gate)
                {
                    return _entries.Count;
                }
            }
        }

        /// <summary>Guards <see cref="_entries"/>, <see cref="_pruneThreshold"/> and the state of every entry.</summary>
        private readonly object _gate = new();
        /// <summary>The entries by key, including expired ones not dropped yet and keys still loading.</summary>
        private readonly Dictionary<TKey, Entry> _entries;
        /// <summary>Returns the current time of the clock expiry is measured on.</summary>
        private readonly Func<TimeSpan> _clock;
        /// <summary>The number of stored entries at which adding a key next drops the expired entries.</summary>
        private int _pruneThreshold = MinPruneThreshold;

        /// <summary>Creates an empty cache that measures expiry on a monotonic clock.</summary>
        /// <param name="timeToLive">How long a stored value stays live; must be positive.</param>
        /// <param name="comparer">The key comparer, or null to use the default comparer.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeToLive"/> is zero or negative.</exception>
        public TtlCache(TimeSpan timeToLive, IEqualityComparer<TKey>? comparer = null)
            : this(timeToLive, comparer, () => s_monotonicClock.Elapsed)
        {
        }

        /// <summary>Creates an empty cache that measures expiry on the given clock.</summary>
        /// <param name="timeToLive">How long a stored value stays live; must be positive.</param>
        /// <param name="comparer">The key comparer, or null to use the default comparer.</param>
        /// <param name="clock">Returns the current time of a clock that never goes back.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeToLive"/> is zero or negative.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="clock"/> is null.</exception>
        internal TtlCache(TimeSpan timeToLive, IEqualityComparer<TKey>? comparer, Func<TimeSpan> clock)
        {
            if (timeToLive <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeToLive), timeToLive, "The time to live must be positive.");
            }
            TimeToLive = timeToLive;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _entries = new Dictionary<TKey, Entry>(comparer);
        }

        /// <summary>Gets the live value of a key.</summary>
        /// <param name="key">The key to look up.</param>
        /// <param name="value">The live value, or the default value when the key has none.</param>
        /// <returns>True when the key has a live value; otherwise false.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out var entry) && entry.IsLive(_clock()))
                {
                    value = entry.Value;
                    return true;
                }
                value = default;
                return false;
            }
        }

        /// <summary>Stores a value that stays live for <see cref="TimeToLive"/>, replacing any value of the key.</summary>
        /// <param name="key">The key to store the value under.</param>
        /// <param name="value">The value.</param>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
        public void Set(TKey key, TValue value)
        {
            lock (_gate)
            {
                GetOrCreateEntry(key).Store(value, _clock() + TimeToLive);
            }
        }

        /// <summary>Removes a key; a load still running for it is not cached when it completes.</summary>
        /// <param name="key">The key to remove.</param>
        /// <returns>True when the key was stored, live or not; otherwise false.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
        public bool Remove(TKey key)
        {
            lock (_gate)
            {
                return _entries.Remove(key);
            }
        }

        /// <summary>Removes every key that matches a condition; loads still running for them are not cached when they complete.</summary>
        /// <param name="predicate">Returns true for a key to remove. It runs under the cache's lock and must not use the cache.</param>
        /// <returns>The number of keys removed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        public int RemoveWhere(Func<TKey, bool> predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }
            lock (_gate)
            {
                var keys = new List<TKey>();
                foreach (var key in _entries.Keys)
                {
                    if (predicate(key))
                    {
                        keys.Add(key);
                    }
                }
                foreach (var key in keys)
                {
                    _entries.Remove(key);
                }
                return keys.Count;
            }
        }

        /// <summary>Removes every key; loads still running are not cached when they complete.</summary>
        public void Clear()
        {
            lock (_gate)
            {
                _entries.Clear();
                _pruneThreshold = MinPruneThreshold;
            }
        }

        /// <summary>Drops the entries whose value expired and that no caller is loading.</summary>
        /// <returns>The number of entries dropped.</returns>
        public int RemoveExpired()
        {
            lock (_gate)
            {
                return RemoveExpiredLocked();
            }
        }

        /// <summary>
        /// Returns the live value of a key, or loads it with a factory and stores it for <see cref="TimeToLive"/>. While a
        /// key loads, other callers for it wait and then reuse the loaded value.
        /// </summary>
        /// <param name="key">The key to look up.</param>
        /// <param name="factory">Loads the value of a key; an exception it throws stores nothing and reaches the caller.</param>
        /// <param name="token">The token canceling the wait for another caller's load; it is also passed to the factory.</param>
        /// <returns>A task returning the live or loaded value.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="factory"/> is null.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="token"/> was canceled while waiting for another caller's load.</exception>
        public ValueTask<TValue> GetOrAddAsync(TKey key, Func<TKey, CancellationToken, ValueTask<TValue>> factory, CancellationToken token = default)
        {
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            Entry entry;
            lock (_gate)
            {
                entry = GetOrCreateEntry(key);
                if (entry.IsLive(_clock()))
                {
                    return new ValueTask<TValue>(entry.Value);
                }
                entry.Loaders++;
            }
            return LoadAsync(key, entry, factory, true, token);
        }

        /// <summary>
        /// Loads the value of a key with a factory and stores it for <see cref="TimeToLive"/>, replacing a live value. A load
        /// for the key that is already running completes first.
        /// </summary>
        /// <param name="key">The key to load.</param>
        /// <param name="factory">Loads the value of a key; an exception it throws keeps the stored value and reaches the caller.</param>
        /// <param name="token">The token canceling the wait for another caller's load; it is also passed to the factory.</param>
        /// <returns>A task returning the loaded value.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="factory"/> is null.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="token"/> was canceled while waiting for another caller's load.</exception>
        public ValueTask<TValue> RefreshAsync(TKey key, Func<TKey, CancellationToken, ValueTask<TValue>> factory, CancellationToken token = default)
        {
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            Entry entry;
            lock (_gate)
            {
                entry = GetOrCreateEntry(key);
                entry.Loaders++;
            }
            return LoadAsync(key, entry, factory, false, token);
        }

        /// <summary>Waits for the entry's load slot, then reuses a value loaded meanwhile or loads and stores a new one.</summary>
        /// <param name="key">The key to load.</param>
        /// <param name="entry">The entry of the key, already counted in <see cref="Entry.Loaders"/>.</param>
        /// <param name="factory">Loads the value of the key.</param>
        /// <param name="reuseLiveValue">Whether a value that became live while waiting is returned instead of loading.</param>
        /// <param name="token">The token canceling the wait; it is also passed to the factory.</param>
        /// <returns>A task returning the value.</returns>
        /// <exception cref="OperationCanceledException"><paramref name="token"/> was canceled while waiting.</exception>
        private async ValueTask<TValue> LoadAsync(TKey key, Entry entry, Func<TKey, CancellationToken, ValueTask<TValue>> factory, bool reuseLiveValue, CancellationToken token)
        {
            try
            {
                await entry.LoadSlot.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (reuseLiveValue)
                    {
                        lock (_gate)
                        {
                            if (entry.IsLive(_clock()))
                            {
                                return entry.Value;
                            }
                        }
                    }
                    var value = await factory(key, token).ConfigureAwait(false);
                    lock (_gate)
                    {
                        // An entry that was removed meanwhile is no longer reachable, so storing into it caches nothing.
                        entry.Store(value, _clock() + TimeToLive);
                    }
                    return value;
                }
                finally
                {
                    entry.LoadSlot.Release();
                }
            }
            finally
            {
                lock (_gate)
                {
                    entry.Loaders--;
                }
            }
        }

        /// <summary>Gets the entry of a key, creating it and first dropping expired entries when the cache has grown. Must be called while holding the lock.</summary>
        /// <param name="key">The key.</param>
        /// <returns>The entry stored under the key.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
        private Entry GetOrCreateEntry(TKey key)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                return entry;
            }
            if (_entries.Count >= _pruneThreshold)
            {
                RemoveExpiredLocked();
                // Doubling the threshold keeps the scans amortized constant per added key.
                _pruneThreshold = Math.Max(MinPruneThreshold, _entries.Count * 2);
            }
            entry = new Entry();
            _entries.Add(key, entry);
            return entry;
        }

        /// <summary>Drops the entries whose value expired and that no caller is loading. Must be called while holding the lock.</summary>
        /// <returns>The number of entries dropped.</returns>
        private int RemoveExpiredLocked()
        {
            var now = _clock();
            var expired = new List<TKey>();
            foreach (var (key, entry) in _entries)
            {
                if (!entry.IsLive(now) && entry.Loaders == 0)
                {
                    expired.Add(key);
                }
            }
            foreach (var key in expired)
            {
                _entries.Remove(key);
            }
            return expired.Count;
        }

        /// <summary>The value of a key and the state of its loads; every member except <see cref="LoadSlot"/> is guarded by the cache's lock.</summary>
        private sealed class Entry
        {
            /// <summary>Gets the stored value; meaningful only while <see cref="HasValue"/> is true.</summary>
            public TValue Value { get; private set; } = default!;

            /// <summary>Gets whether a value was stored.</summary>
            public bool HasValue { get; private set; }

            /// <summary>Gets the clock time at which the stored value expires.</summary>
            public TimeSpan ExpiresAt { get; private set; }

            /// <summary>Gets or sets the number of callers waiting for or running a load of the key.</summary>
            public int Loaders { get; set; }

            /// <summary>Gets the slot that lets one load of the key run at a time; never disposed, since it never creates a wait handle.</summary>
            public SemaphoreSlim LoadSlot { get; } = new SemaphoreSlim(1, 1);

            /// <summary>Gets whether the stored value is live at a clock time.</summary>
            /// <param name="now">The clock time.</param>
            /// <returns>True when a value was stored and has not expired.</returns>
            public bool IsLive(TimeSpan now)
            {
                return HasValue && now < ExpiresAt;
            }

            /// <summary>Stores a value.</summary>
            /// <param name="value">The value.</param>
            /// <param name="expiresAt">The clock time at which the value expires.</param>
            public void Store(TValue value, TimeSpan expiresAt)
            {
                Value = value;
                HasValue = true;
                ExpiresAt = expiresAt;
            }
        }
    }
}
