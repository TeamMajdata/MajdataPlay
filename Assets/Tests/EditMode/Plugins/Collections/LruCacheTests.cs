using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MajdataPlay.Collections;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Collections
{
    /// <summary>Covers lookup, recency, eviction and release rules of <see cref="LruCache{TKey, TValue}"/>.</summary>
    public class LruCacheTests
    {
        /// <summary>The values passed to the release callback of the cache under test, in release order.</summary>
        private List<KeyValuePair<string, int>> _released = null!;

        /// <summary>Creates an empty release log.</summary>
        [SetUp]
        public void SetUp()
        {
            _released = new List<KeyValuePair<string, int>>();
        }

        /// <summary>Creates a cache that records every released entry in <see cref="_released"/>.</summary>
        /// <param name="capacity">The cache capacity.</param>
        /// <returns>The cache.</returns>
        private LruCache<string, int> CreateCache(int capacity)
        {
            return new LruCache<string, int>(capacity, (key, value) => _released.Add(new KeyValuePair<string, int>(key, value)));
        }

        /// <summary>Checks that a capacity below one is rejected.</summary>
        [Test]
        public void Constructor_RejectsCapacityBelowOne()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(-1));
            Assert.That(new LruCache<string, int>(1).Capacity, Is.EqualTo(1));
        }

        /// <summary>Checks that stored values are found and missing keys report the default value.</summary>
        [Test]
        public void TryGetValue_ReturnsStoredValues()
        {
            var cache = CreateCache(2);
            cache.Set("a", 1);

            Assert.That(cache.TryGetValue("a", out var value), Is.True);
            Assert.That(value, Is.EqualTo(1));
            Assert.That(cache.TryGetValue("missing", out var missing), Is.False);
            Assert.That(missing, Is.EqualTo(0));
            Assert.That(cache.Count, Is.EqualTo(1));
        }

        /// <summary>Checks that a new key over the capacity releases the least recently stored entry.</summary>
        [Test]
        public void Set_OverCapacity_ReleasesLeastRecentlyUsed()
        {
            var cache = CreateCache(2);
            cache.Set("a", 1);
            cache.Set("b", 2);

            cache.Set("c", 3);

            Assert.That(_released, Is.EqualTo(new[] { new KeyValuePair<string, int>("a", 1) }));
            Assert.That(cache.ContainsKey("a"), Is.False);
            Assert.That(cache.ContainsKey("b"), Is.True);
            Assert.That(cache.ContainsKey("c"), Is.True);
            Assert.That(cache.Count, Is.EqualTo(2));
        }

        /// <summary>Checks that reading a value protects it from the next eviction.</summary>
        [Test]
        public void TryGetValue_MarksEntryAsMostRecentlyUsed()
        {
            var cache = CreateCache(2);
            cache.Set("a", 1);
            cache.Set("b", 2);

            cache.TryGetValue("a", out _);
            cache.Set("c", 3);

            Assert.That(_released.Single().Key, Is.EqualTo("b"));
            Assert.That(cache.ContainsKey("a"), Is.True);
        }

        /// <summary>Checks that peeking and containment checks do not change which entry is evicted next.</summary>
        [Test]
        public void TryPeekAndContainsKey_DoNotChangeRecency()
        {
            var cache = CreateCache(2);
            cache.Set("a", 1);
            cache.Set("b", 2);

            Assert.That(cache.TryPeek("a", out var value), Is.True);
            Assert.That(value, Is.EqualTo(1));
            Assert.That(cache.ContainsKey("a"), Is.True);
            Assert.That(cache.TryPeek("missing", out _), Is.False);
            cache.Set("c", 3);

            Assert.That(_released.Single().Key, Is.EqualTo("a"));
        }

        /// <summary>Checks that replacing a key releases the old value, keeps the count and refreshes the entry.</summary>
        [Test]
        public void Set_ExistingKey_ReplacesAndReleasesOldValue()
        {
            var cache = CreateCache(2);
            cache.Set("a", 1);
            cache.Set("b", 2);

            cache.Set("a", 10);

            Assert.That(_released, Is.EqualTo(new[] { new KeyValuePair<string, int>("a", 1) }));
            Assert.That(cache.Count, Is.EqualTo(2));
            Assert.That(cache.TryPeek("a", out var value), Is.True);
            Assert.That(value, Is.EqualTo(10));

            cache.Set("c", 3);
            Assert.That(_released.Last().Key, Is.EqualTo("b"));
        }

        /// <summary>Checks that storing the same value again under its key does not release it.</summary>
        [Test]
        public void Set_SameValue_DoesNotRelease()
        {
            var cache = CreateCache(2);
            cache.Set("a", 1);

            cache.Set("a", 1);

            Assert.That(_released, Is.Empty);
            Assert.That(cache.Count, Is.EqualTo(1));
        }

        /// <summary>Checks that removing an entry releases it and that removing a missing key does nothing.</summary>
        [Test]
        public void Remove_ReleasesValue()
        {
            var cache = CreateCache(2);
            cache.Set("a", 1);

            Assert.That(cache.Remove("a"), Is.True);
            Assert.That(cache.Remove("a"), Is.False);

            Assert.That(_released, Is.EqualTo(new[] { new KeyValuePair<string, int>("a", 1) }));
            Assert.That(cache.Count, Is.Zero);
        }

        /// <summary>Checks that taking an entry out hands it to the caller without releasing it.</summary>
        [Test]
        public void TryRemove_ReturnsValueWithoutReleasing()
        {
            var cache = CreateCache(2);
            cache.Set("a", 1);

            Assert.That(cache.TryRemove("a", out var value), Is.True);
            Assert.That(value, Is.EqualTo(1));
            Assert.That(cache.TryRemove("a", out var missing), Is.False);
            Assert.That(missing, Is.EqualTo(0));

            Assert.That(_released, Is.Empty);
            Assert.That(cache.ContainsKey("a"), Is.False);
        }

        /// <summary>Checks that clearing releases every entry from the least to the most recently used one.</summary>
        [Test]
        public void Clear_ReleasesAllInRecencyOrder()
        {
            var cache = CreateCache(3);
            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Set("c", 3);
            cache.TryGetValue("a", out _);

            cache.Clear();

            Assert.That(_released.Select(x => x.Key), Is.EqualTo(new[] { "b", "c", "a" }));
            Assert.That(cache.Count, Is.Zero);
            cache.Set("d", 4);
            Assert.That(cache.Count, Is.EqualTo(1));
        }

        /// <summary>Checks that a failing release callback does not stop the other entries from being released.</summary>
        [Test]
        public void Clear_ReleasesRemainingEntriesWhenACallbackThrows()
        {
            var released = new List<string>();
            var cache = new LruCache<string, int>(3, (key, _) =>
            {
                released.Add(key);
                if (key == "a")
                {
                    throw new InvalidOperationException("release failed");
                }
            });
            cache.Set("a", 1);
            cache.Set("b", 2);

            var exception = Assert.Throws<AggregateException>(() => cache.Clear());

            Assert.That(exception!.InnerExceptions.Single(), Is.InstanceOf<InvalidOperationException>());
            Assert.That(released, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(cache.Count, Is.Zero);
        }

        /// <summary>Checks that the cache works without a release callback.</summary>
        [Test]
        public void WithoutCallback_EvictsSilently()
        {
            var cache = new LruCache<string, int>(1);
            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Remove("b");
            cache.Set("c", 3);
            cache.Clear();

            Assert.That(cache.Count, Is.Zero);
        }

        /// <summary>Checks that the supplied key comparer is used.</summary>
        [Test]
        public void Comparer_IsUsedForKeys()
        {
            var cache = new LruCache<string, int>(2, null, StringComparer.OrdinalIgnoreCase);
            cache.Set("Key", 1);

            Assert.That(cache.TryGetValue("KEY", out var value), Is.True);
            Assert.That(value, Is.EqualTo(1));
        }

        /// <summary>Checks that null keys are rejected.</summary>
        [Test]
        public void NullKey_Throws()
        {
            var cache = CreateCache(1);

            Assert.Throws<ArgumentNullException>(() => cache.Set(null!, 1));
            Assert.Throws<ArgumentNullException>(() => cache.TryGetValue(null!, out _));
            Assert.Throws<ArgumentNullException>(() => cache.Remove(null!));
        }

        /// <summary>
        /// Checks that the release callback runs outside the cache's lock: another thread can use the cache while it runs.
        /// </summary>
        [Test]
        public void ReleaseCallback_RunsOutsideTheLock()
        {
            LruCache<string, int>? cache = null;
            var otherThreadFinished = false;
            cache = new LruCache<string, int>(1, (_, _) =>
            {
                // Waiting here would deadlock if the cache still held its lock while releasing.
                otherThreadFinished = Task.Run(() => cache!.Count).Wait(TimeSpan.FromSeconds(5));
            });
            cache.Set("a", 1);

            cache.Set("b", 2);

            Assert.That(otherThreadFinished, Is.True);
        }

        /// <summary>
        /// Checks that concurrent use keeps the capacity and releases every value that left the cache exactly once.
        /// </summary>
        [Test]
        public void ConcurrentUse_ReleasesEachValueOnce()
        {
            const int Capacity = 16;
            const int ThreadCount = 8;
            const int SetsPerThread = 2000;
            var released = new ConcurrentBag<int>();
            var cache = new LruCache<string, int>(Capacity, (_, value) => released.Add(value));

            Parallel.For(0, ThreadCount, thread =>
            {
                for (var i = 0; i < SetsPerThread; i++)
                {
                    // Unique values with a small key space, so threads keep replacing and evicting each other's entries.
                    var value = (thread * SetsPerThread) + i;
                    cache.Set($"key{value % 40}", value);
                    cache.TryGetValue($"key{(value + 7) % 40}", out _);
                }
            });

            Assert.That(cache.Count, Is.EqualTo(Capacity));
            var cached = new List<int>();
            for (var i = 0; i < 40; i++)
            {
                if (cache.TryPeek($"key{i}", out var value))
                {
                    cached.Add(value);
                }
            }
            var all = released.Concat(cached).ToList();
            Assert.That(all.Count, Is.EqualTo(ThreadCount * SetsPerThread));
            Assert.That(all.Distinct().Count(), Is.EqualTo(all.Count));
        }
    }
}
