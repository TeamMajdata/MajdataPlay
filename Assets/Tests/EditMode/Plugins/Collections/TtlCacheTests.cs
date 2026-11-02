using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Collections
{
    /// <summary>Covers expiry, removal, loading and pruning rules of <see cref="TtlCache{TKey, TValue}"/>.</summary>
    public class TtlCacheTests
    {
        /// <summary>The time to live of the caches under test.</summary>
        private static readonly TimeSpan s_timeToLive = TimeSpan.FromMinutes(30);

        /// <summary>The current time of the fake clock the caches under test read.</summary>
        private TimeSpan _now;

        /// <summary>The number of times a factory created by <see cref="CountingFactory(int)"/> ran.</summary>
        private int _loads;

        /// <summary>Starts the fake clock and the load count at zero.</summary>
        [SetUp]
        public void SetUp()
        {
            _now = TimeSpan.FromHours(1);
            _loads = 0;
        }

        /// <summary>Creates a cache that reads the fake clock.</summary>
        /// <returns>The cache.</returns>
        private TtlCache<string, int> CreateCache()
        {
            return new TtlCache<string, int>(s_timeToLive, null, () => _now);
        }

        /// <summary>Creates a factory that counts its runs in <see cref="_loads"/> and returns a value.</summary>
        /// <param name="value">The value the factory returns.</param>
        /// <returns>The factory.</returns>
        private Func<string, CancellationToken, ValueTask<int>> CountingFactory(int value)
        {
            return (key, token) =>
            {
                Interlocked.Increment(ref _loads);
                return new ValueTask<int>(value);
            };
        }

        /// <summary>Creates a factory that counts its runs in <see cref="_loads"/> and completes when a gate is opened.</summary>
        /// <param name="gate">Completes the load with its value.</param>
        /// <returns>The factory.</returns>
        private Func<string, CancellationToken, ValueTask<int>> GatedFactory(TaskCompletionSource<int> gate)
        {
            return (key, token) =>
            {
                Interlocked.Increment(ref _loads);
                return new ValueTask<int>(gate.Task);
            };
        }

        /// <summary>Checks that a time to live of zero or less is rejected.</summary>
        [Test]
        public void Constructor_RejectsNonPositiveTimeToLive()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TtlCache<string, int>(TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TtlCache<string, int>(TimeSpan.FromSeconds(-1)));
            Assert.That(new TtlCache<string, int>(s_timeToLive).TimeToLive, Is.EqualTo(s_timeToLive));
        }

        /// <summary>Checks that a stored value is live until exactly its time to live has passed.</summary>
        [Test]
        public void Set_ValueExpiresAfterTimeToLive()
        {
            var cache = CreateCache();
            cache.Set("a", 1);

            _now += s_timeToLive - TimeSpan.FromTicks(1);
            Assert.That(cache.TryGetValue("a", out var live), Is.True);
            Assert.That(live, Is.EqualTo(1));
            Assert.That(cache.Count, Is.EqualTo(1));

            _now += TimeSpan.FromTicks(1);
            Assert.That(cache.TryGetValue("a", out var expired), Is.False);
            Assert.That(expired, Is.EqualTo(0));
            Assert.That(cache.Count, Is.EqualTo(0));
        }

        /// <summary>Checks that storing a value again replaces it and restarts its time to live.</summary>
        [Test]
        public void Set_ReplacesValueAndRestartsExpiry()
        {
            var cache = CreateCache();
            cache.Set("a", 1);
            _now += TimeSpan.FromMinutes(20);
            cache.Set("a", 2);
            _now += TimeSpan.FromMinutes(20);

            Assert.That(cache.TryGetValue("a", out var value), Is.True);
            Assert.That(value, Is.EqualTo(2));
        }

        /// <summary>Checks removal of one key, of the keys matching a condition and of every key.</summary>
        [Test]
        public void Remove_RemoveWhere_Clear_DropKeys()
        {
            var cache = CreateCache();
            cache.Set("a/1", 1);
            cache.Set("a/2", 2);
            cache.Set("b/1", 3);
            cache.Set("c/1", 4);

            Assert.That(cache.Remove("c/1"), Is.True);
            Assert.That(cache.Remove("c/1"), Is.False);
            Assert.That(cache.RemoveWhere(key => key.StartsWith("a/")), Is.EqualTo(2));
            Assert.That(cache.TryGetValue("a/1", out _), Is.False);
            Assert.That(cache.TryGetValue("b/1", out _), Is.True);

            cache.Clear();
            Assert.That(cache.Count, Is.EqualTo(0));
            Assert.That(cache.StoredCount, Is.EqualTo(0));
            Assert.Throws<ArgumentNullException>(() => cache.RemoveWhere(null!));
        }

        /// <summary>Checks that a loaded value is reused while live and loaded again once it expired.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOrAddAsync_LoadsOnceWhileLive()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var cache = CreateCache();

                Assert.That(await cache.GetOrAddAsync("a", CountingFactory(1)), Is.EqualTo(1));
                Assert.That(await cache.GetOrAddAsync("a", CountingFactory(2)), Is.EqualTo(1));
                Assert.That(_loads, Is.EqualTo(1));

                _now += s_timeToLive;
                Assert.That(await cache.GetOrAddAsync("a", CountingFactory(3)), Is.EqualTo(3));
                Assert.That(_loads, Is.EqualTo(2));
                Assert.That(cache.TryGetValue("a", out var value) && value == 3, Is.True);
            });
        }

        /// <summary>Checks that callers asking for a key while it loads wait for that load instead of starting their own.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOrAddAsync_ConcurrentCallersShareOneLoad()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var cache = CreateCache();
                var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

                var first = cache.GetOrAddAsync("a", GatedFactory(gate));
                var second = cache.GetOrAddAsync("a", GatedFactory(gate));
                gate.SetResult(7);

                Assert.That(await first, Is.EqualTo(7));
                Assert.That(await second, Is.EqualTo(7));
                Assert.That(_loads, Is.EqualTo(1));
            });
        }

        /// <summary>Checks that a failed load stores nothing, reaches its caller and lets the next caller load again.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOrAddAsync_FailedLoadIsNotCached()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var cache = CreateCache();
                var failure = default(Exception);

                try
                {
                    await cache.GetOrAddAsync("a", (key, token) => throw new InvalidOperationException("offline"));
                }
                catch (InvalidOperationException exception)
                {
                    failure = exception;
                }

                Assert.That(failure, Is.Not.Null);
                Assert.That(cache.Count, Is.EqualTo(0));
                Assert.That(await cache.GetOrAddAsync("a", CountingFactory(5)), Is.EqualTo(5));
                Assert.That(_loads, Is.EqualTo(1));
                Assert.Throws<ArgumentNullException>(() => cache.GetOrAddAsync("a", null!));
            });
        }

        /// <summary>Checks that a refresh loads and replaces a live value, while a failed refresh keeps it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RefreshAsync_ReplacesLiveValue()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var cache = CreateCache();
                cache.Set("a", 1);

                Assert.That(await cache.RefreshAsync("a", CountingFactory(2)), Is.EqualTo(2));
                Assert.That(cache.TryGetValue("a", out var refreshed) && refreshed == 2, Is.True);

                var failed = false;
                try
                {
                    await cache.RefreshAsync("a", (key, token) => throw new InvalidOperationException("offline"));
                }
                catch (InvalidOperationException)
                {
                    failed = true;
                }
                Assert.That(failed, Is.True);
                Assert.That(cache.TryGetValue("a", out var kept) && kept == 2, Is.True);
            });
        }

        /// <summary>Checks that removing a key while it loads keeps the loaded value out of the cache.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Remove_DuringLoad_DoesNotCacheLoadedValue()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var cache = CreateCache();
                var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                var load = cache.GetOrAddAsync("a", GatedFactory(gate));

                cache.Remove("a");
                gate.SetResult(7);

                Assert.That(await load, Is.EqualTo(7), "The caller still gets the value it loaded.");
                Assert.That(cache.TryGetValue("a", out _), Is.False);
                Assert.That(await cache.GetOrAddAsync("a", CountingFactory(8)), Is.EqualTo(8));
            });
        }

        /// <summary>Checks that a caller whose wait is canceled gives up without stopping the load it waited for.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOrAddAsync_CanceledWait_LeavesLoadRunning()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var cache = CreateCache();
                var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var canceled = new CancellationTokenSource();
                var load = cache.GetOrAddAsync("a", GatedFactory(gate));
                var waiting = cache.GetOrAddAsync("a", GatedFactory(gate), canceled.Token);

                canceled.Cancel();
                var failure = default(Exception);
                try
                {
                    await waiting;
                }
                catch (OperationCanceledException exception)
                {
                    failure = exception;
                }
                gate.SetResult(7);

                Assert.That(failure, Is.Not.Null);
                Assert.That(await load, Is.EqualTo(7));
                Assert.That(cache.TryGetValue("a", out var value) && value == 7, Is.True);
                Assert.That(_loads, Is.EqualTo(1));
            });
        }

        /// <summary>Checks that expired entries are dropped on request and once enough new keys arrive, but never while they load.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RemoveExpired_DropsExpiredEntriesExceptLoadingOnes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var cache = CreateCache();
                for (var i = 0; i < 64; i++)
                {
                    cache.Set(i.ToString(), i);
                }
                _now += s_timeToLive;
                Assert.That(cache.StoredCount, Is.EqualTo(64));

                cache.Set("new", 1);
                Assert.That(cache.StoredCount, Is.EqualTo(1), "Adding a key to a grown cache drops the expired entries.");

                var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                var load = cache.GetOrAddAsync("loading", GatedFactory(gate));
                _now += s_timeToLive;
                Assert.That(cache.RemoveExpired(), Is.EqualTo(1));
                Assert.That(cache.StoredCount, Is.EqualTo(1), "A key that is loading stays.");

                gate.SetResult(3);
                Assert.That(await load, Is.EqualTo(3));
                Assert.That(cache.TryGetValue("loading", out var value) && value == 3, Is.True);
            });
        }
    }
}
