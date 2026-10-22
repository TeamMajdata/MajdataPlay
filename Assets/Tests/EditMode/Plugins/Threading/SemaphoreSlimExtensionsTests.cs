using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using MajdataPlay.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Threading
{
    /// <summary>Covers the scoped locking helpers in <see cref="SemaphoreSlimExtensions"/>.</summary>
    public class SemaphoreSlimExtensionsTests
    {
        /// <summary>Checks that the disposable wrapper rejects a null semaphore.</summary>
        [Test]
        public void Constructor_NullSemaphore_Throws()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new SemaphoreSlimExtensions.SemaphoreSlimDisposable(null!));

            Assert.That(exception!.ParamName, Is.EqualTo("lock"));
        }

        /// <summary>Checks that Lock acquires the semaphore until the scope ends.</summary>
        [Test]
        public void Lock_HoldsSemaphoreForScope()
        {
            using var semaphore = new SemaphoreSlim(1, 1);

            using (semaphore.Lock())
            {
                Assert.That(semaphore.CurrentCount, Is.EqualTo(0));
            }

            Assert.That(semaphore.CurrentCount, Is.EqualTo(1));
        }

        /// <summary>Checks that AsDisposable does not acquire by itself but its Lock and Dispose acquire and release.</summary>
        [Test]
        public void AsDisposable_LockAndDispose_AcquireAndRelease()
        {
            using var semaphore = new SemaphoreSlim(1, 1);
            var scope = semaphore.AsDisposable();

            Assert.That(semaphore.CurrentCount, Is.EqualTo(1));
            scope.Lock();
            Assert.That(semaphore.CurrentCount, Is.EqualTo(0));
            scope.Dispose();
            Assert.That(semaphore.CurrentCount, Is.EqualTo(1));
        }

        /// <summary>Checks that disposing a scope that never acquired a full semaphore fails.</summary>
        [Test]
        public void Dispose_WithoutLock_OnFullSemaphore_Throws()
        {
            using var semaphore = new SemaphoreSlim(1, 1);
            var scope = semaphore.AsDisposable();

            Assert.Throws<SemaphoreFullException>(() => scope.Dispose());
        }

        /// <summary>Checks that LockAsync acquires the semaphore until the scope ends.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LockAsync_HoldsSemaphoreForScope() => UniTask.ToCoroutine(async () =>
        {
            using var semaphore = new SemaphoreSlim(1, 1);

            using (await semaphore.LockAsync())
            {
                Assert.That(semaphore.CurrentCount, Is.EqualTo(0));
            }

            Assert.That(semaphore.CurrentCount, Is.EqualTo(1));
        });

        /// <summary>Checks that LockAsync waits for a held semaphore and acquires it after release.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LockAsync_WaitsForRelease() => UniTask.ToCoroutine(async () =>
        {
            using var semaphore = new SemaphoreSlim(1, 1);
            var held = semaphore.Lock();

            var pending = semaphore.LockAsync().AsTask();
            await UniTask.Yield();
            Assert.That(pending.IsCompleted, Is.False);

            held.Dispose();
            using (await pending)
            {
                Assert.That(semaphore.CurrentCount, Is.EqualTo(0));
            }

            Assert.That(semaphore.CurrentCount, Is.EqualTo(1));
        });

        /// <summary>Checks that a canceled LockAsync fails without acquiring the semaphore.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LockAsync_Canceled_Throws() => UniTask.ToCoroutine(async () =>
        {
            using var semaphore = new SemaphoreSlim(0, 1);
            using var cancellation = new CancellationTokenSource();
            var pending = semaphore.LockAsync(cancellation.Token).AsTask();

            cancellation.Cancel();
            var canceled = false;
            try
            {
                await pending;
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            Assert.That(canceled, Is.True);
            Assert.That(semaphore.CurrentCount, Is.EqualTo(0));
        });

        /// <summary>Checks that the wrapper's own LockAsync acquires the semaphore.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator WrapperLockAsync_Acquires() => UniTask.ToCoroutine(async () =>
        {
            using var semaphore = new SemaphoreSlim(1, 1);
            var scope = semaphore.AsDisposable();

            await scope.LockAsync();

            Assert.That(semaphore.CurrentCount, Is.EqualTo(0));
            scope.Dispose();
            Assert.That(semaphore.CurrentCount, Is.EqualTo(1));
        });
    }
}
