using System;
using System.Collections.Generic;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Base
{
    /// <summary>Covers the process-wide key/value store <see cref="MajCache{TKey, TValue}"/>.</summary>
    public class MajCacheTests
    {
        /// <summary>The unique key used by the current test, so no production entry is touched.</summary>
        private Guid _key;

        /// <summary>Creates a fresh key for each test.</summary>
        [SetUp]
        public void SetUp()
        {
            _key = Guid.NewGuid();
        }

        /// <summary>Removes the entry written by the test.</summary>
        [TearDown]
        public void TearDown()
        {
            MajCache<Guid, string>.TryRemove(_key, out _);
        }

        /// <summary>Checks that GetOrAdd stores the first value and returns it on later calls.</summary>
        [Test]
        public void GetOrAdd_KeepsFirstValue()
        {
            Assert.That(MajCache<Guid, string>.GetOrAdd(_key, "first"), Is.EqualTo("first"));
            Assert.That(MajCache<Guid, string>.GetOrAdd(_key, "second"), Is.EqualTo("first"));
        }

        /// <summary>Checks that the key/value pair overload behaves like the two-argument overload.</summary>
        [Test]
        public void GetOrAdd_Pair_KeepsFirstValue()
        {
            var first = new KeyValuePair<Guid, string>(_key, "first");
            var second = new KeyValuePair<Guid, string>(_key, "second");

            Assert.That(MajCache<Guid, string>.GetOrAdd(in first), Is.EqualTo("first"));
            Assert.That(MajCache<Guid, string>.GetOrAdd(in second), Is.EqualTo("first"));
        }

        /// <summary>Checks that Replace overwrites existing values through both overloads.</summary>
        [Test]
        public void Replace_OverwritesValue()
        {
            MajCache<Guid, string>.GetOrAdd(_key, "first");

            MajCache<Guid, string>.Replace(_key, "second");
            Assert.That(MajCache<Guid, string>.GetValue(_key), Is.EqualTo("second"));

            var pair = new KeyValuePair<Guid, string>(_key, "third");
            MajCache<Guid, string>.Replace(in pair);
            Assert.That(MajCache<Guid, string>.GetValue(_key), Is.EqualTo("third"));
        }

        /// <summary>Checks that TryGetValue and TryRemove report presence correctly.</summary>
        [Test]
        public void TryGetValueAndTryRemove_ReportPresence()
        {
            Assert.That(MajCache<Guid, string>.TryGetValue(_key, out _), Is.False);

            MajCache<Guid, string>.Replace(_key, "value");

            Assert.That(MajCache<Guid, string>.TryGetValue(_key, out var stored), Is.True);
            Assert.That(stored, Is.EqualTo("value"));
            Assert.That(MajCache<Guid, string>.TryRemove(_key, out var removed), Is.True);
            Assert.That(removed, Is.EqualTo("value"));
            Assert.That(MajCache<Guid, string>.TryRemove(_key, out _), Is.False);
        }

        /// <summary>Checks that reading a missing key throws.</summary>
        [Test]
        public void GetValue_MissingKey_Throws()
        {
            Assert.Throws<KeyNotFoundException>(() => MajCache<Guid, string>.GetValue(_key));
        }
    }
}
