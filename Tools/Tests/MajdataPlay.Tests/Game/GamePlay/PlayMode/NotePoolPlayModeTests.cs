using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Game.Notes;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.GamePlay.PlayMode.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.PlayMode
{
    /// <summary>Checks the timed activation, recycling and disposal of <see cref="NotePool{TInfo, TMember}"/> and <see cref="EachLinePool"/>.</summary>
    public class NotePoolPlayModeTests
    {
        /// <summary>Tracks objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>The parent of the pooled clones.</summary>
        private Transform _parent = null!;

        /// <summary>The pools created by a test, disposed in tear down.</summary>
        private readonly List<IDisposable> _pools = new();

        /// <summary>Creates the pool parent.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _parent = _objects.Create("Pool").transform;
        }

        /// <summary>Disposes every pool and destroys the objects.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (var pool in _pools)
            {
                pool.Dispose();
            }
            _pools.Clear();
            _objects.Dispose();
        }

        /// <summary>Checks that the pool instantiates and activates one clone per slot.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Constructor_InstantiatesTheCapacity()
        {
            var pool = CreatePool(3, Info(1f));
            yield return null;

            Assert.That(pool.Capacity, Is.EqualTo(3));
            Assert.That(pool.IsStatic, Is.True);
            Assert.That(_parent.childCount, Is.EqualTo(3));
            Assert.That(Probes().All(x => x.gameObject.activeSelf), Is.True);
        }

        /// <summary>Checks that notes are activated 0.15 s before their appear timing, in timing order.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnPreUpdate_ActivatesNotesShortlyBeforeTheyAppear()
        {
            var early = Info(1f);
            var together = Info(1f);
            var late = Info(2f);
            var pool = CreatePool(4, late, early, together);
            yield return null;

            pool.OnPreUpdate(0.5f);
            Assert.That(InitializedInfos(), Is.Empty);

            pool.OnPreUpdate(0.9f);
            Assert.That(InitializedInfos(), Is.EquivalentTo(new[] { early, together }));
            Assert.That(early.Instance, Is.Null, "The probe is not a note drop");

            pool.OnPreUpdate(0.9f);
            Assert.That(InitializedInfos(), Has.Count.EqualTo(2), "Activated timing points are consumed");

            pool.OnPreUpdate(5f);
            Assert.That(InitializedInfos(), Is.EquivalentTo(new[] { early, together, late }));
        }

        /// <summary>Checks that notes wait for a free slot and resume after a note is collected.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnPreUpdate_WaitsForAFreeSlot()
        {
            var first = Info(1f);
            var second = Info(1f);
            var third = Info(1f);
            var pool = CreatePool(2, first, second, third);
            yield return null;

            pool.OnPreUpdate(1f);
            pool.OnPreUpdate(1f);
            Assert.That(InitializedInfos(), Has.Count.EqualTo(2));

            var used = Probes().First(x => x.InitializedWith.Count > 0);
            pool.Collect(used);
            pool.OnPreUpdate(1f);

            Assert.That(InitializedInfos(), Is.EquivalentTo(new[] { first, second, third }));
            Assert.That(used.InitializedWith, Has.Count.EqualTo(2), "The collected note is reused");
        }

        /// <summary>Checks renting and returning idle notes directly, including returning to a full pool.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DequeueAndCollect_RentAndReturnNotes()
        {
            var pool = CreatePool(2, Info(1f));
            yield return null;

            var a = pool.Dequeue();
            var b = pool.Dequeue();
            var none = pool.Dequeue();
            var stillNone = pool.Dequeue();

            Assert.That(a, Is.Not.Null);
            Assert.That(b, Is.Not.Null);
            Assert.That(a, Is.Not.SameAs(b));
            Assert.That(none, Is.Null);
            Assert.That(stillNone, Is.Null);

            pool.Collect(a!);
            Assert.That(pool.Dequeue(), Is.SameAs(a));

            pool.Collect(a!);
            pool.Collect(b!);
            pool.Collect(b!);
            Assert.That(pool.Dequeue(), Is.Not.Null);
            Assert.That(pool.Dequeue(), Is.Not.Null);
            Assert.That(pool.Dequeue(), Is.Null, "Returning to a full pool is ignored");
        }

        /// <summary>Checks that disposing destroys the clones and rejects later use.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Dispose_DestroysTheClones()
        {
            var pool = CreatePool(2, Info(1f));
            var probe = Probes()[0];
            yield return null;

            pool.Dispose();
            pool.Dispose();
            yield return null;

            Assert.That(_parent.childCount, Is.EqualTo(0));
            Assert.Throws<ObjectDisposedException>(() => pool.OnPreUpdate(1f));
            Assert.Throws<ObjectDisposedException>(() => pool.Dequeue());
            Assert.Throws<ObjectDisposedException>(() => pool.Collect(probe));
        }

        /// <summary>Checks that an each line pool only activates each line drops and rejects other notes on collect.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator EachLinePool_RequiresEachLineDrops()
        {
            var prefab = _objects.Create("EachLinePrefab");
            var probePrefab = prefab.AddComponent<GamePlayEachLineProbe>();
            var info = new EachLinePoolingInfo { AppearTiming = 1f, Timing = 1f, DistanceProvider = new EachLineBinding() };
            var pool = new EachLinePool(prefab, _parent, new[] { info }, 1);
            _pools.Add(pool);
            var inactive = _objects.Create("Inactive");
            inactive.SetActive(false);
            var eachLine = inactive.AddComponent<EachLineDrop>();
            yield return null;

            pool.OnPreUpdate(0f);
            Assert.That(_parent.GetComponentInChildren<GamePlayEachLineProbe>().InitCount, Is.EqualTo(0), "Too early");

            pool.OnPreUpdate(1f);
            pool.OnPreUpdate(1f);
            Assert.That(_parent.GetComponentInChildren<GamePlayEachLineProbe>().InitCount, Is.EqualTo(0), "A probe is not an each line drop");

            Assert.Throws<ArgumentException>(() => pool.Collect(probePrefab));
            Assert.DoesNotThrow(() => pool.Collect(eachLine));

            pool.Dispose();
            Assert.Throws<ObjectDisposedException>(() => pool.OnPreUpdate(1f));
            Assert.Throws<ObjectDisposedException>(() => pool.Collect(eachLine));
        }

        /// <summary>Checks that an each line pool created with the default capacity has 64 slots.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator EachLinePool_DefaultCapacityIs64()
        {
            var prefab = _objects.Create("EachLinePrefab");
            prefab.AddComponent<GamePlayEachLineProbe>();
            var pool = new EachLinePool(prefab, _parent, Array.Empty<EachLinePoolingInfo>());
            _pools.Add(pool);
            yield return null;

            pool.OnPreUpdate(10f);

            Assert.That(pool.Capacity, Is.EqualTo(64));
            Assert.That(_parent.childCount, Is.EqualTo(64));
        }

        /// <summary>Creates a tap pool whose prefab is a recording probe.</summary>
        /// <param name="capacity">The number of slots.</param>
        /// <param name="infos">The pooling infos.</param>
        /// <returns>The pool.</returns>
        private NotePool<TapPoolingInfo, TapQueueInfo> CreatePool(int capacity, params TapPoolingInfo[] infos)
        {
            var prefab = _objects.Create("Prefab");
            prefab.AddComponent<GamePlayPoolableNoteProbe>();
            prefab.SetActive(false);
            var pool = new NotePool<TapPoolingInfo, TapQueueInfo>(prefab, _parent, infos, capacity);
            _pools.Add(pool);
            return pool;
        }

        /// <summary>Gets the probes instantiated under the pool parent.</summary>
        /// <returns>The probes in child order.</returns>
        private List<GamePlayPoolableNoteProbe> Probes()
        {
            var probes = new List<GamePlayPoolableNoteProbe>();
            for (var i = 0; i < _parent.childCount; i++)
            {
                probes.Add(_parent.GetChild(i).GetComponent<GamePlayPoolableNoteProbe>());
            }
            return probes;
        }

        /// <summary>Gets every pooling info the probes were initialized with.</summary>
        /// <returns>The infos.</returns>
        private List<TapPoolingInfo> InitializedInfos()
        {
            return Probes().SelectMany(x => x.InitializedWith).ToList();
        }

        /// <summary>Creates a tap pooling info.</summary>
        /// <param name="appearTiming">The appear timing.</param>
        /// <returns>The info.</returns>
        private static TapPoolingInfo Info(float appearTiming)
        {
            return new TapPoolingInfo { AppearTiming = appearTiming, Timing = appearTiming + 1f };
        }
    }
}
