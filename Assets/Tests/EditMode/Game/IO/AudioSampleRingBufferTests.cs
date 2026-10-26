using System;
using MajdataPlay.IO;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>Verifies the bounded interleaved storage of <see cref="AudioSampleRingBuffer"/>.</summary>
    public class AudioSampleRingBufferTests
    {
        /// <summary>Checks that invalid layouts are rejected.</summary>
        [Test]
        public void Constructor_InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSampleRingBuffer(0, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSampleRingBuffer(2, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSampleRingBuffer(4, int.MaxValue));
        }

        /// <summary>Checks the reported layout of a new buffer.</summary>
        [Test]
        public void Constructor_ReportsLayout()
        {
            var buffer = new AudioSampleRingBuffer(2, 8);

            Assert.That(buffer.Channels, Is.EqualTo(2));
            Assert.That(buffer.CapacityFrames, Is.EqualTo(8));
            Assert.That(buffer.AvailableFrames, Is.EqualTo(0));
            Assert.That(buffer.DroppedFrames, Is.EqualTo(0));
        }

        /// <summary>Checks that float frames are read back in order and a trailing partial frame is ignored.</summary>
        [Test]
        public void WriteFloat_ThenRead_ReturnsFramesInOrder()
        {
            var buffer = new AudioSampleRingBuffer(2, 8);

            var stored = buffer.Write(new[] { 1f, 2f, 3f, 4f, 5f }, 2);
            var destination = new float[8];
            var read = buffer.Read(destination);

            Assert.That(stored, Is.EqualTo(2));
            Assert.That(read, Is.EqualTo(4));
            Assert.That(destination.AsSpan(0, 4).ToArray(), Is.EqualTo(new[] { 1f, 2f, 3f, 4f }));
            Assert.That(buffer.AvailableFrames, Is.EqualTo(0));
        }

        /// <summary>Checks that only the leading channels of a wider source are kept.</summary>
        [Test]
        public void WriteFloat_WiderSource_KeepsLeadingChannels()
        {
            var buffer = new AudioSampleRingBuffer(2, 4);

            buffer.Write(new[] { 1f, 2f, 9f, 9f, 3f, 4f, 9f, 9f }, 4);
            var destination = new float[4];
            buffer.Read(destination);

            Assert.That(destination, Is.EqualTo(new[] { 1f, 2f, 3f, 4f }));
        }

        /// <summary>Checks that signed 16-bit samples are scaled to the nominal float range.</summary>
        [Test]
        public void WriteShort_ScalesSamples()
        {
            var buffer = new AudioSampleRingBuffer(1, 4);

            buffer.Write(new short[] { short.MinValue, 0, 16384 }, 1);
            var destination = new float[3];
            buffer.Read(destination);

            Assert.That(destination, Is.EqualTo(new[] { -1f, 0f, 0.5f }));
        }

        /// <summary>Checks that a source with fewer channels than the buffer is rejected for both sample formats.</summary>
        [Test]
        public void Write_TooFewSourceChannels_Throws()
        {
            var buffer = new AudioSampleRingBuffer(2, 4);

            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Write(new float[4], 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Write(new short[4], 1));
        }

        /// <summary>Checks that frames that do not fit are dropped and counted.</summary>
        [Test]
        public void Write_Overflow_DropsNewestFrames()
        {
            var buffer = new AudioSampleRingBuffer(1, 3);

            var stored = buffer.Write(new[] { 1f, 2f, 3f, 4f, 5f }, 1);
            var shortStored = buffer.Write(new short[] { 1 }, 1);
            var destination = new float[5];
            var read = buffer.Read(destination);

            Assert.That(stored, Is.EqualTo(3));
            Assert.That(shortStored, Is.EqualTo(0));
            Assert.That(buffer.DroppedFrames, Is.EqualTo(3));
            Assert.That(read, Is.EqualTo(3));
            Assert.That(destination.AsSpan(0, 3).ToArray(), Is.EqualTo(new[] { 1f, 2f, 3f }));
        }

        /// <summary>Checks that writes and reads wrap around the end of the storage.</summary>
        [Test]
        public void WriteAndRead_WrapAroundTheStorage()
        {
            var buffer = new AudioSampleRingBuffer(1, 4);
            buffer.Write(new[] { 1f, 2f, 3f }, 1);
            var first = new float[2];
            buffer.Read(first);

            buffer.Write(new[] { 4f, 5f, 6f }, 1);
            var second = new float[4];
            var read = buffer.Read(second);

            Assert.That(first, Is.EqualTo(new[] { 1f, 2f }));
            Assert.That(read, Is.EqualTo(4));
            Assert.That(second, Is.EqualTo(new[] { 3f, 4f, 5f, 6f }));
        }

        /// <summary>Checks that short frames wrap around the end of the storage too.</summary>
        [Test]
        public void WriteShort_WrapsAroundTheStorage()
        {
            var buffer = new AudioSampleRingBuffer(1, 2);
            buffer.Write(new short[] { 0 }, 1);
            buffer.Read(new float[1]);

            buffer.Write(new short[] { 16384, -16384 }, 1);
            var destination = new float[2];
            buffer.Read(destination);

            Assert.That(destination, Is.EqualTo(new[] { 0.5f, -0.5f }));
        }

        /// <summary>Checks that silence clears previously used storage, wraps and counts dropped frames.</summary>
        [Test]
        public void WriteSilence_ClearsWrapsAndCountsDrops()
        {
            var buffer = new AudioSampleRingBuffer(1, 4);
            buffer.Write(new[] { 7f, 7f, 7f }, 1);
            buffer.Read(new float[3]);
            buffer.Write(new[] { 1f }, 1);

            var stored = buffer.WriteSilence(5);
            var destination = new float[4];
            buffer.Read(destination);

            Assert.That(stored, Is.EqualTo(3));
            Assert.That(buffer.DroppedFrames, Is.EqualTo(2));
            Assert.That(destination, Is.EqualTo(new[] { 1f, 0f, 0f, 0f }));
        }

        /// <summary>Checks that silence spanning the end of the storage clears both parts.</summary>
        [Test]
        public void WriteSilence_WrapsAroundTheStorage()
        {
            var buffer = new AudioSampleRingBuffer(1, 4);
            buffer.Write(new[] { 9f, 9f, 9f, 9f }, 1);
            buffer.Read(new float[2]);
            buffer.Read(new float[2]);
            buffer.Write(new[] { 1f, 2f }, 1);
            buffer.Read(new float[2]);

            var stored = buffer.WriteSilence(4);
            var destination = new float[4];
            buffer.Read(destination);

            Assert.That(stored, Is.EqualTo(4));
            Assert.That(destination, Is.EqualTo(new[] { 0f, 0f, 0f, 0f }));
        }

        /// <summary>Checks that a negative amount of silence is rejected and zero is accepted.</summary>
        [Test]
        public void WriteSilence_NegativeOrZero()
        {
            var buffer = new AudioSampleRingBuffer(1, 4);

            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.WriteSilence(-1));
            Assert.That(buffer.WriteSilence(0), Is.EqualTo(0));
            Assert.That(buffer.AvailableFrames, Is.EqualTo(0));
        }

        /// <summary>Checks that reads only return whole frames and report nothing for an empty or too small destination.</summary>
        [Test]
        public void Read_ReturnsWholeFramesOnly()
        {
            var buffer = new AudioSampleRingBuffer(2, 4);
            Assert.That(buffer.Read(new float[8]), Is.EqualTo(0));

            buffer.Write(new[] { 1f, 2f, 3f, 4f }, 2);

            Assert.That(buffer.Read(new float[1]), Is.EqualTo(0));
            var destination = new float[3];
            Assert.That(buffer.Read(destination), Is.EqualTo(2));
            Assert.That(destination.AsSpan(0, 2).ToArray(), Is.EqualTo(new[] { 1f, 2f }));
            Assert.That(buffer.AvailableFrames, Is.EqualTo(1));
        }

        /// <summary>Checks that clearing discards frames and resets the drop counter.</summary>
        [Test]
        public void Clear_DiscardsFramesAndDrops()
        {
            var buffer = new AudioSampleRingBuffer(1, 1);
            buffer.Write(new[] { 1f, 2f }, 1);

            buffer.Clear();

            Assert.That(buffer.AvailableFrames, Is.EqualTo(0));
            Assert.That(buffer.DroppedFrames, Is.EqualTo(0));
            Assert.That(buffer.Write(new[] { 3f }, 1), Is.EqualTo(1));
        }
    }
}
