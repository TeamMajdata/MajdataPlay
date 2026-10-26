using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
#nullable enable
namespace MajdataPlay.Collections
{
    /// <summary>
    /// A thread-safe, fixed-capacity cache that discards its least recently used entry when a new key would exceed the capacity.
    /// </summary>
    /// <remarks>
    /// The cache owns every stored value. A value leaves the cache through capacity eviction, replacement,
    /// <see cref="Remove(TKey)"/> or <see cref="Clear"/>, and each of these passes it to the release callback exactly once.
    /// <see cref="TryRemove(TKey, out TValue)"/> instead hands ownership back to the caller without releasing.
    /// The release callback always runs outside the cache's lock, on the thread that caused the value to leave.
    /// </remarks>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The cached value type.</typeparam>
    public sealed class LruCache<TKey, TValue> where TKey : notnull
    {
        /// <summary>Gets the maximum number of entries the cache keeps.</summary>
        public int Capacity { get; }

        /// <summary>Gets the number of entries currently cached.</summary>
        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _entries.Count;
                }
            }
        }

        readonly object _gate = new();
        readonly Dictionary<TKey, LinkedListNode<Entry>> _entries;
        // The first node is the most recently used entry, the last node is the next one to evict.
        readonly LinkedList<Entry> _recency = new();
        readonly Action<TKey, TValue>? _onReleased;

        /// <summary>Creates an empty cache.</summary>
        /// <param name="capacity">The maximum number of entries to keep; must be at least 1.</param>
        /// <param name="onReleased">
        /// Called with the key and value of every entry the cache discards, for example to free native resources.
        /// It is not called for values handed back by <see cref="TryRemove(TKey, out TValue)"/>.
        /// </param>
        /// <param name="comparer">The key comparer, or null to use the default comparer.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is less than 1.</exception>
        public LruCache(int capacity, Action<TKey, TValue>? onReleased = null, IEqualityComparer<TKey>? comparer = null)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "The capacity must be at least 1.");
            }
            Capacity = capacity;
            _onReleased = onReleased;
            _entries = new Dictionary<TKey, LinkedListNode<Entry>>(capacity, comparer);
        }

        /// <summary>Gets a cached value and marks its entry as the most recently used one.</summary>
        /// <param name="key">The key to look up.</param>
        /// <param name="value">The cached value, or the default value when the key is not cached.</param>
        /// <returns>True when the key is cached; otherwise false.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(key, out var node))
                {
                    value = default;
                    return false;
                }
                MoveToFront(node);
                value = node.Value.Value;
                return true;
            }
        }

        /// <summary>Gets a cached value without changing which entry is evicted next.</summary>
        /// <param name="key">The key to look up.</param>
        /// <param name="value">The cached value, or the default value when the key is not cached.</param>
        /// <returns>True when the key is cached; otherwise false.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
        public bool TryPeek(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(key, out var node))
                {
                    value = default;
                    return false;
                }
                value = node.Value.Value;
                return true;
            }
        }

        /// <summary>Checks whether a key is cached without changing which entry is evicted next.</summary>
        /// <param name="key">The key to look up.</param>
        /// <returns>True when the key is cached; otherwise false.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
        public bool ContainsKey(TKey key)
        {
            lock (_gate)
            {
                return _entries.ContainsKey(key);
            }
        }

        /// <summary>
        /// Stores a value as the most recently used entry. A different value previously stored under the same key is released;
        /// when a new key exceeds the capacity, the least recently used entry is released.
        /// </summary>
        /// <param name="key">The key to store the value under.</param>
        /// <param name="value">The value the cache takes ownership of.</param>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
        /// <exception cref="Exception">The release callback threw; the cache has already been updated.</exception>
        public void Set(TKey key, TValue value)
        {
            var hasReleased = false;
            var releasedKey = default(TKey);
            var releasedValue = default(TValue);
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out var node))
                {
                    var oldValue = node.Value.Value;
                    node.Value = new Entry(key, value);
                    MoveToFront(node);
                    if (!EqualityComparer<TValue>.Default.Equals(oldValue, value))
                    {
                        hasReleased = true;
                        releasedKey = key;
                        releasedValue = oldValue;
                    }
                }
                else
                {
                    if (_entries.Count >= Capacity)
                    {
                        // Reuse the evicted node so a full cache stores new entries without allocating.
                        node = _recency.Last!;
                        _recency.RemoveLast();
                        _entries.Remove(node.Value.Key);
                        hasReleased = true;
                        releasedKey = node.Value.Key;
                        releasedValue = node.Value.Value;
                        node.Value = new Entry(key, value);
                        _recency.AddFirst(node);
                    }
                    else
                    {
                        node = _recency.AddFirst(new Entry(key, value));
                    }
                    _entries.Add(key, node);
                }
            }
            if (hasReleased)
            {
                _onReleased?.Invoke(releasedKey!, releasedValue!);
            }
        }

        /// <summary>Removes an entry and releases its value.</summary>
        /// <param name="key">The key to remove.</param>
        /// <returns>True when the key was cached; otherwise false.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
        /// <exception cref="Exception">The release callback threw; the entry has already been removed.</exception>
        public bool Remove(TKey key)
        {
            if (!TryRemove(key, out var value))
            {
                return false;
            }
            _onReleased?.Invoke(key, value);
            return true;
        }

        /// <summary>Removes an entry and hands its value back to the caller without releasing it.</summary>
        /// <param name="key">The key to remove.</param>
        /// <param name="value">The removed value, now owned by the caller, or the default value when the key is not cached.</param>
        /// <returns>True when the key was cached; otherwise false.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
        public bool TryRemove(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            lock (_gate)
            {
                if (!_entries.Remove(key, out var node))
                {
                    value = default;
                    return false;
                }
                _recency.Remove(node);
                value = node.Value.Value;
                return true;
            }
        }

        /// <summary>Removes every entry and releases each value, from the least to the most recently used one.</summary>
        /// <exception cref="AggregateException">One or more release callbacks threw; every entry has still been removed and offered for release.</exception>
        public void Clear()
        {
            Entry[] released;
            lock (_gate)
            {
                released = new Entry[_entries.Count];
                var index = 0;
                for (var node = _recency.Last; node is not null; node = node.Previous)
                {
                    released[index++] = node.Value;
                }
                _entries.Clear();
                _recency.Clear();
            }
            if (_onReleased is null)
            {
                return;
            }
            List<Exception>? exceptions = null;
            foreach (var entry in released)
            {
                try
                {
                    _onReleased(entry.Key, entry.Value);
                }
                catch (Exception e)
                {
                    exceptions ??= new List<Exception>();
                    exceptions.Add(e);
                }
            }
            if (exceptions is not null)
            {
                throw new AggregateException("One or more cached values could not be released.", exceptions);
            }
        }

        /// <summary>Marks a node as the most recently used entry. Must be called while holding the lock.</summary>
        /// <param name="node">The node of an entry stored in this cache.</param>
        void MoveToFront(LinkedListNode<Entry> node)
        {
            if (node == _recency.First)
            {
                return;
            }
            _recency.Remove(node);
            _recency.AddFirst(node);
        }

        /// <summary>A cached key and value pair.</summary>
        readonly struct Entry
        {
            /// <summary>Gets the key the value is stored under.</summary>
            public TKey Key { get; }

            /// <summary>Gets the cached value.</summary>
            public TValue Value { get; }

            /// <summary>Creates an entry.</summary>
            /// <param name="key">The key the value is stored under.</param>
            /// <param name="value">The cached value.</param>
            public Entry(TKey key, TValue value)
            {
                Key = key;
                Value = value;
            }
        }
    }
}
