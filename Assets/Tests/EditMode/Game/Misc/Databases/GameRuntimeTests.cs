using System.Collections;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Databases;
using MajdataPlay.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Databases
{
    /// <summary>Covers the runtime database singleton <see cref="GameRuntime"/> and the native object liveness helpers.</summary>
    public class GameRuntimeTests
    {
        /// <summary>Whether the runtime database was loaded before the test.</summary>
        private bool _wasLoaded;

        /// <summary>The objects created by the test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Records whether the runtime database was already loaded.</summary>
        [SetUp]
        public void SetUp()
        {
            _wasLoaded = !Majdata<GameRuntime>.IsNull;
            _objects = new TestObjects();
        }

        /// <summary>Unloads the runtime database again when the test loaded it.</summary>
        [TearDown]
        public void TearDown()
        {
            if (!_wasLoaded)
            {
                Majdata<GameRuntime>.Free();
            }
            _objects.Dispose();
        }

        /// <summary>Checks that the runtime database is loaded from Resources once and exposes its assets.</summary>
        [Test]
        public void Init_LoadsRuntimeDatabase()
        {
            GameRuntime.Init();
            var first = GameRuntime.Instance;
            GameRuntime.Init();

            Assert.That(first, Is.Not.Null);
            Assert.That(GameRuntime.Instance, Is.SameAs(first));
            Assert.That(Majdata<GameRuntime>.IsSingleton, Is.True);
            Assert.That(first.DifficultyColors.Length, Is.GreaterThanOrEqualTo(7));
            Assert.That(first.Sprite, Is.Not.Null);
            Assert.That(first.Note, Is.Not.Null);
            Assert.That(first.LocalizedFonts, Is.Not.Null);
            Assert.That(first.Sprite.EmptySongCover, Is.Not.Null);
            Assert.That(first.LocalizedFonts.Default, Is.Not.Null);
        }

        /// <summary>Checks that liveness reflects destruction of the native object on the main thread and from a worker thread.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator IsNativeAlive_TracksDestruction()
        {
            return UniTask.ToCoroutine(async () =>
            {
                // The synchronous check posts to the recorded main thread; it would wait forever if another thread was recorded.
                Assume.That(MajEnv.MainThread, Is.SameAs(System.Threading.Thread.CurrentThread));
                var alive = _objects.Create("Alive");
                var destroyed = new GameObject("Destroyed");
                Object.DestroyImmediate(destroyed);

                Assert.That(alive.IsNativeAlive(), Is.True);
                Assert.That(destroyed.IsNativeAlive(), Is.False);
                Assert.That(await alive.IsNativeAliveAsync(), Is.True);
                Assert.That(await destroyed.IsNativeAliveAsync(), Is.False);

                var fromWorker = await UniTask.RunOnThreadPool(() => alive.IsNativeAlive());
                Assert.That(fromWorker, Is.True);
            });
        }
    }
}
