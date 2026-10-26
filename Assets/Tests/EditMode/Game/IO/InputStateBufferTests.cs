using System;
using MajdataPlay.IO;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>Verifies the live-state and frame-snapshot separation of <see cref="InputStateBuffer"/>.</summary>
    public class InputStateBufferTests
    {
        /// <summary>Checks that a negative size is rejected.</summary>
        [Test]
        public void Constructor_NegativeCount_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new InputStateBuffer(-1));
        }

        /// <summary>Checks that published states are only visible in the snapshot after the frame update.</summary>
        [Test]
        public void Publish_IsVisibleAfterPreUpdate()
        {
            var buffer = new InputStateBuffer(3);
            var snapshot = Read(buffer, 3);
            Assert.That(snapshot.States, Is.EqualTo(new[] { false, false, false }));

            buffer.Publish(new[] { true, false, true });
            Assert.That(Read(buffer, 3).States, Is.EqualTo(new[] { false, false, false }), "before the frame update");
            Assert.That(buffer.IsCurrentlyOn(0), Is.True, "the live state is updated immediately");

            buffer.OnPreUpdate();
            snapshot = Read(buffer, 3);
            Assert.That(snapshot.States, Is.EqualTo(new[] { true, false, true }));
            Assert.That(snapshot.HadOn, Is.EqualTo(new[] { true, false, true }));
            Assert.That(snapshot.HadOff, Is.EqualTo(new[] { false, true, false }));
        }

        /// <summary>Checks that a short pulse between two frame updates is retained as an edge.</summary>
        [Test]
        public void Pulse_BetweenFrames_IsLatched()
        {
            var buffer = new InputStateBuffer(1);
            buffer.Publish(new[] { true });
            buffer.Publish(new[] { false });

            buffer.OnPreUpdate();
            var snapshot = Read(buffer, 1);

            Assert.That(snapshot.States[0], Is.False);
            Assert.That(snapshot.HadOn[0], Is.True);
            Assert.That(snapshot.HadOff[0], Is.True);
        }

        /// <summary>Checks that the edges are consumed by a frame update.</summary>
        [Test]
        public void PreUpdate_ConsumesPendingEdges()
        {
            var buffer = new InputStateBuffer(1);
            buffer.Publish(new[] { true });
            buffer.OnPreUpdate();

            buffer.OnPreUpdate();
            var snapshot = Read(buffer, 1);

            Assert.That(snapshot.States[0], Is.True);
            Assert.That(snapshot.HadOn[0], Is.False);
            Assert.That(snapshot.HadOff[0], Is.False);
        }

        /// <summary>Checks that clearing releases every live state and latches a release edge.</summary>
        [Test]
        public void Clear_ReleasesEverything()
        {
            var buffer = new InputStateBuffer(2);
            buffer.Publish(new[] { true, true });
            buffer.OnPreUpdate();

            buffer.Clear();
            Assert.That(buffer.IsCurrentlyOn(0), Is.False);
            buffer.OnPreUpdate();
            var snapshot = Read(buffer, 2);

            Assert.That(snapshot.States, Is.EqualTo(new[] { false, false }));
            Assert.That(snapshot.HadOff, Is.EqualTo(new[] { true, true }));
        }

        /// <summary>Checks that publishing the wrong number of states is rejected.</summary>
        [Test]
        public void Publish_WrongLength_Throws()
        {
            var buffer = new InputStateBuffer(2);

            Assert.Throws<ArgumentException>(() => buffer.Publish(new bool[3]));
        }

        /// <summary>Checks that copying into destinations that are too short is rejected.</summary>
        [Test]
        public void CopyTo_ShortDestination_Throws()
        {
            var buffer = new InputStateBuffer(2);

            Assert.Throws<ArgumentException>(() => buffer.CopyTo(new bool[1], new bool[2], new bool[2]));
            Assert.Throws<ArgumentException>(() => buffer.CopyTo(new bool[2], new bool[1], new bool[2]));
            Assert.Throws<ArgumentException>(() => buffer.CopyTo(new bool[2], new bool[2], new bool[1]));
        }

        /// <summary>Checks that larger destinations are accepted.</summary>
        [Test]
        public void CopyTo_LargerDestination_CopiesThePrefix()
        {
            var buffer = new InputStateBuffer(1);
            buffer.Publish(new[] { true });
            buffer.OnPreUpdate();

            var snapshot = Read(buffer, 3);

            Assert.That(snapshot.States, Is.EqualTo(new[] { true, false, false }));
        }

        /// <summary>Checks that out of range indices report released.</summary>
        [Test]
        public void IsCurrentlyOn_OutOfRange_ReturnsFalse()
        {
            var buffer = new InputStateBuffer(1);
            buffer.Publish(new[] { true });

            Assert.That(buffer.IsCurrentlyOn(-1), Is.False);
            Assert.That(buffer.IsCurrentlyOn(1), Is.False);
        }

        /// <summary>Copies a snapshot out of a buffer.</summary>
        /// <param name="buffer">The buffer to read.</param>
        /// <param name="length">The destination length.</param>
        /// <returns>The states and edges.</returns>
        private static (bool[] States, bool[] HadOn, bool[] HadOff) Read(InputStateBuffer buffer, int length)
        {
            var states = new bool[length];
            var hadOn = new bool[length];
            var hadOff = new bool[length];
            buffer.CopyTo(states, hadOn, hadOff);
            return (states, hadOn, hadOff);
        }
    }
}
