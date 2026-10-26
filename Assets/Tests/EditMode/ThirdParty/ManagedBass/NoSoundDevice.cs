using System;
using System.IO;
using System.Text;
using ManagedBass;

#nullable enable
namespace MajdataPlay.Tests.ThirdParty.ManagedBass
{
    /// <summary>Initializes BASS on its silent "no sound" device and builds test audio for decoding channels.</summary>
    internal static class NoSoundDevice
    {
        /// <summary>The sample rate of the test audio.</summary>
        public const int SampleRate = 44100;

        /// <summary>The number of interleaved channels of the test audio.</summary>
        public const int Channels = 2;

        /// <summary>The number of bytes in one second of 16-bit test audio.</summary>
        public const int BytesPerSecond = SampleRate * Channels * sizeof(short);

        /// <summary>Whether this helper initialized BASS and must free it.</summary>
        private static bool s_ownsDevice;

        /// <summary>Initializes BASS on the silent device, which never opens real audio hardware.</summary>
        /// <exception cref="InvalidOperationException">BASS could not be initialized.</exception>
        public static void Init()
        {
            if (Bass.Init(0, SampleRate, DeviceInitFlags.Default))
            {
                s_ownsDevice = true;
                return;
            }
            if (Bass.LastError != Errors.Already)
            {
                throw new InvalidOperationException($"BASS could not be initialized: {Bass.LastError}");
            }
            Bass.CurrentDevice = 0;
        }

        /// <summary>Frees BASS when <see cref="Init"/> initialized it.</summary>
        public static void Free()
        {
            if (s_ownsDevice)
            {
                Bass.Free();
                s_ownsDevice = false;
            }
        }

        /// <summary>Builds 16-bit stereo PCM samples that differ from frame to frame.</summary>
        /// <param name="frames">The number of frames.</param>
        /// <returns>The interleaved little-endian sample bytes.</returns>
        public static byte[] CreatePcm(int frames)
        {
            var pcm = new byte[frames * Channels * sizeof(short)];
            for (var i = 0; i < pcm.Length / sizeof(short); i++)
            {
                var sample = (short)((i * 37) % 20000 - 10000);
                pcm[i * 2] = (byte)sample;
                pcm[(i * 2) + 1] = (byte)(sample >> 8);
            }
            return pcm;
        }

        /// <summary>Wraps 16-bit stereo PCM samples in a WAV file.</summary>
        /// <param name="pcm">The interleaved little-endian sample bytes.</param>
        /// <returns>The WAV file bytes.</returns>
        public static byte[] CreateWav(byte[] pcm)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.ASCII);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + pcm.Length);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)Channels);
            writer.Write(SampleRate);
            writer.Write(BytesPerSecond);
            writer.Write((short)(Channels * sizeof(short)));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(pcm.Length);
            writer.Write(pcm);
            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>Pulls sample data from a decoding channel, which runs its callbacks on the calling thread.</summary>
        /// <param name="handle">The decoding channel.</param>
        /// <param name="length">The number of bytes to pull.</param>
        /// <returns>The number of bytes BASS returned.</returns>
        public static int Pull(int handle, int length)
        {
            var buffer = new byte[4096];
            var total = 0;
            while (total < length)
            {
                var read = Bass.ChannelGetData(handle, buffer, Math.Min(buffer.Length, length - total));
                if (read <= 0)
                {
                    break;
                }
                total += read;
            }
            return total;
        }

        /// <summary>Decodes a decoding channel to its end.</summary>
        /// <param name="handle">The decoding channel.</param>
        /// <returns>The decoded bytes.</returns>
        public static byte[] ReadToEnd(int handle)
        {
            using var decoded = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = Bass.ChannelGetData(handle, buffer, buffer.Length)) > 0)
            {
                decoded.Write(buffer, 0, read);
            }
            return decoded.ToArray();
        }
    }
}
