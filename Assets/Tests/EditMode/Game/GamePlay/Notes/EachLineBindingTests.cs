using System;
using MajdataPlay.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Tests;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Notes
{
    /// <summary>Checks how <see cref="EachLineBinding"/> chooses the note that drives an each line.</summary>
    public class EachLineBindingTests
    {
        /// <summary>Tracks objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Creates the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
        }

        /// <summary>Destroys every object created by the test.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Checks that an unbound line has no distance and no ended note.</summary>
        [Test]
        public void Unbound_HasNoDistance()
        {
            IEachLineDistanceProvider binding = new EachLineBinding();

            Assert.That(binding.Distance, Is.EqualTo(0f));
            Assert.That(binding.IsAnyNoteEnded, Is.False);
        }

        /// <summary>Checks that a tap replaces a hold as the distance provider but not another tap.</summary>
        [Test]
        public void Bind_PrefersTheFirstTapOverHolds()
        {
            var binding = new EachLineBinding();
            var provider = (IEachLineDistanceProvider)binding;
            var hold = CreateHold(1.5f);
            var tap = CreateTap(2.5f);
            var secondTap = CreateTap(3.5f);
            var secondHold = CreateHold(4.5f);

            binding.Bind(hold);
            Assert.That(provider.Distance, Is.EqualTo(1.5f));

            binding.Bind(secondHold);
            Assert.That(provider.Distance, Is.EqualTo(1.5f), "A second hold does not replace the first one");

            binding.Bind(tap);
            Assert.That(provider.Distance, Is.EqualTo(2.5f));

            binding.Bind(secondTap);
            Assert.That(provider.Distance, Is.EqualTo(2.5f));

            tap.Distance = 9f;
            Assert.That(provider.Distance, Is.EqualTo(9f), "The distance is read live from the provider");
        }

        /// <summary>Checks that notes without a distance never become the provider.</summary>
        [Test]
        public void Bind_IgnoresNotesWithoutDistance()
        {
            var binding = new EachLineBinding();
            var touch = _objects.Add<TouchDrop>();

            binding.Bind(touch);

            Assert.That(((IEachLineDistanceProvider)binding).Distance, Is.EqualTo(0f));
        }

        /// <summary>Checks that unbinding marks the line as ended and releases the provider with the last note.</summary>
        [Test]
        public void Unbind_ReleasesTheProviderWithTheLastNote()
        {
            var binding = new EachLineBinding();
            var provider = (IEachLineDistanceProvider)binding;
            var tap = CreateTap(2f);
            var hold = CreateHold(1f);
            binding.Bind(tap);
            binding.Bind(hold);

            binding.Unbind(tap);

            Assert.That(provider.IsAnyNoteEnded, Is.True);
            Assert.That(provider.Distance, Is.EqualTo(2f), "The provider is kept while notes remain");

            binding.Unbind(hold);

            Assert.That(provider.Distance, Is.EqualTo(0f));
        }

        /// <summary>Checks that null notes and notes that were never bound are rejected.</summary>
        [Test]
        public void BindAndUnbind_RejectInvalidNotes()
        {
            var binding = new EachLineBinding();
            var tap = CreateTap(1f);

            Assert.Throws<ArgumentNullException>(() => binding.Bind(null!));
            Assert.Throws<ArgumentNullException>(() => binding.Unbind(null!));
            Assert.Throws<InvalidOperationException>(() => binding.Unbind(tap));
        }

        /// <summary>Checks that binding more than eight notes keeps the first eight tracked.</summary>
        [Test]
        public void Bind_TracksAtMostEightNotes()
        {
            var binding = new EachLineBinding();
            var taps = new TapDrop[9];
            for (var i = 0; i < taps.Length; i++)
            {
                taps[i] = CreateTap(i);
                binding.Bind(taps[i]);
            }

            Assert.Throws<InvalidOperationException>(() => binding.Unbind(taps[8]));
            for (var i = 0; i < 8; i++)
            {
                binding.Unbind(taps[i]);
            }
            Assert.That(((IEachLineDistanceProvider)binding).Distance, Is.EqualTo(0f));
        }

        /// <summary>Creates a tap with a distance.</summary>
        /// <param name="distance">The distance reported by the tap.</param>
        /// <returns>The tap.</returns>
        private TapDrop CreateTap(float distance)
        {
            var tap = _objects.Add<TapDrop>();
            tap.Distance = distance;
            return tap;
        }

        /// <summary>Creates a hold with a distance.</summary>
        /// <param name="distance">The distance reported by the hold.</param>
        /// <returns>The hold.</returns>
        private HoldDrop CreateHold(float distance)
        {
            var hold = _objects.Add<HoldDrop>();
            PrivateMember.SetProperty(hold, nameof(HoldDrop.Distance), distance);
            return hold;
        }
    }
}
