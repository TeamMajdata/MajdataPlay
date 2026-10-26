using System;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Base
{
    /// <summary>Covers the instance slot and singleton state machine of <see cref="Majdata{T}"/>.</summary>
    public class MajdataSingletonTests
    {
        /// <summary>A private type so the generic static slot is not shared with production code.</summary>
        private sealed class SingletonProbe
        {
        }

        /// <summary>Releases the probe slot after each test.</summary>
        [TearDown]
        public void TearDown()
        {
            Majdata<SingletonProbe>.Free();
        }

        /// <summary>Checks that an unused slot holds no instance and is not a singleton.</summary>
        [Test]
        public void Slot_IsInitiallyEmpty()
        {
            Assert.That(Majdata<SingletonProbe>.IsNull, Is.True);
            Assert.That(Majdata<SingletonProbe>.Instance, Is.Null);
            Assert.That(Majdata<SingletonProbe>.IsSingleton, Is.False);
        }

        /// <summary>Checks that the instance setter stores a value without marking it as a singleton.</summary>
        [Test]
        public void InstanceSetter_StoresValue()
        {
            var probe = new SingletonProbe();

            Majdata<SingletonProbe>.Instance = probe;

            Assert.That(Majdata<SingletonProbe>.Instance, Is.SameAs(probe));
            Assert.That(Majdata<SingletonProbe>.IsNull, Is.False);
            Assert.That(Majdata<SingletonProbe>.IsSingleton, Is.False);
        }

        /// <summary>Checks that the instance setter may replace a value that is not a singleton.</summary>
        [Test]
        public void InstanceSetter_ReplacesNonSingletonValue()
        {
            var first = new SingletonProbe();
            var second = new SingletonProbe();

            Majdata<SingletonProbe>.Instance = first;
            Majdata<SingletonProbe>.Instance = second;

            Assert.That(Majdata<SingletonProbe>.Instance, Is.SameAs(second));
        }

        /// <summary>Checks that registering a null singleton is rejected.</summary>
        [Test]
        public void SetAsSingleton_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Majdata<SingletonProbe>.SetAsSingleton(null!));
        }

        /// <summary>Checks that a singleton is stored and flagged.</summary>
        [Test]
        public void SetAsSingleton_StoresAndFlagsInstance()
        {
            var probe = new SingletonProbe();

            Majdata<SingletonProbe>.SetAsSingleton(probe);

            Assert.That(Majdata<SingletonProbe>.Instance, Is.SameAs(probe));
            Assert.That(Majdata<SingletonProbe>.IsSingleton, Is.True);
        }

        /// <summary>Checks that a second singleton registration is rejected.</summary>
        [Test]
        public void SetAsSingleton_Twice_Throws()
        {
            Majdata<SingletonProbe>.SetAsSingleton(new SingletonProbe());

            Assert.Throws<InvalidOperationException>(() => Majdata<SingletonProbe>.SetAsSingleton(new SingletonProbe()));
        }

        /// <summary>Checks that the instance setter cannot overwrite a registered singleton.</summary>
        [Test]
        public void InstanceSetter_AfterSingleton_Throws()
        {
            Majdata<SingletonProbe>.SetAsSingleton(new SingletonProbe());

            Assert.Throws<InvalidOperationException>(() => Majdata<SingletonProbe>.Instance = new SingletonProbe());
        }

        /// <summary>Checks that freeing a singleton clears it so a new singleton can be registered.</summary>
        [Test]
        public void Free_ReleasesSingleton()
        {
            Majdata<SingletonProbe>.SetAsSingleton(new SingletonProbe());

            Majdata<SingletonProbe>.Free();

            Assert.That(Majdata<SingletonProbe>.IsNull, Is.True);
            Assert.That(Majdata<SingletonProbe>.IsSingleton, Is.False);
            var replacement = new SingletonProbe();
            Majdata<SingletonProbe>.SetAsSingleton(replacement);
            Assert.That(Majdata<SingletonProbe>.Instance, Is.SameAs(replacement));
        }
    }
}
