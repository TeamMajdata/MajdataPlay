using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MajdataPlay.Utils;
using ManagedBass;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Utils
{
    /// <summary>Verifies the format probing of <see cref="BassHelper"/> on the silent BASS device.</summary>
    public class BassHelperTests
    {
        /// <summary>Whether this fixture initialized BASS and must free it.</summary>
        private bool _ownsDevice;

        /// <summary>Initializes BASS on the silent device, which never opens audio hardware.</summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _ownsDevice = Bass.Init(0, 44100, DeviceInitFlags.Default);
            if (!_ownsDevice)
            {
                Assert.That(Bass.LastError, Is.EqualTo(Errors.Already));
                Bass.CurrentDevice = 0;
            }
        }

        /// <summary>Frees BASS when the fixture initialized it.</summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_ownsDevice)
            {
                Bass.Free();
            }
        }

        /// <summary>Checks that a WAV buffer is decoded by the first format probe.</summary>
        [Test]
        public void CreateStream_ManagedBuffer_DecodesWav()
        {
            var wav = CreateWav(1000);

            var stream = BassHelper.CreateStream(wav, 0, wav.LongLength, BassFlags.Decode);
            try
            {
                Assert.That(stream, Is.Not.Zero);
                Assert.That(Bass.ChannelGetInfo(stream).Frequency, Is.EqualTo(44100));
                Assert.That(Bass.ChannelGetLength(stream), Is.EqualTo(1000 * 2));
            }
            finally
            {
                Bass.StreamFree(stream);
            }
        }

        /// <summary>Checks that a WAV buffer in pinned memory is decoded by the pointer overload.</summary>
        [Test]
        public void CreateStream_PinnedBuffer_DecodesWav()
        {
            var wav = CreateWav(500);
            var handle = GCHandle.Alloc(wav, GCHandleType.Pinned);
            try
            {
                var stream = BassHelper.CreateStream(handle.AddrOfPinnedObject(), 0, wav.LongLength, BassFlags.Decode);
                Assert.That(stream, Is.Not.Zero);
                Bass.StreamFree(stream);
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>Checks that data no probe recognises ends with a range error or the BASS error of the last probe.</summary>
        [Test]
        public void CreateStream_UnknownFormat_Throws()
        {
            var garbage = new byte[64];
            var handle = GCHandle.Alloc(garbage, GCHandleType.Pinned);
            try
            {
                Assert.That(() => BassHelper.CreateStream(garbage, 0, garbage.LongLength, BassFlags.Decode),
                    Throws.InstanceOf<ArgumentOutOfRangeException>().Or.InstanceOf<BassException>());
                Assert.That(() => BassHelper.CreateStream(handle.AddrOfPinnedObject(), 0, garbage.LongLength, BassFlags.Decode),
                    Throws.InstanceOf<ArgumentOutOfRangeException>().Or.InstanceOf<BassException>());
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>Builds a mono 16-bit WAV file of silence.</summary>
        /// <param name="frames">The number of frames.</param>
        /// <returns>The WAV bytes.</returns>
        private static byte[] CreateWav(int frames)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.ASCII);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + (frames * 2));
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(44100);
            writer.Write(44100 * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(frames * 2);
            writer.Write(new byte[frames * 2]);
            writer.Flush();
            return stream.ToArray();
        }
    }
}
