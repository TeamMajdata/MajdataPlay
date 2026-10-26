using System;
using System.IO;
using System.Text;
using ManagedBass;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>Builds in-memory WAV files and initializes BASS on its silent "no sound" device for audio tests.</summary>
    internal static class BassTestSupport
    {
        /// <summary>The sample rate of generated test audio.</summary>
        public const int SampleRate = 44100;

        /// <summary>Gets a value indicating whether this helper initialized BASS and must free it.</summary>
        public static bool OwnsDevice { get; private set; }

        /// <summary>Initializes BASS on the silent device, which never opens real audio hardware.</summary>
        /// <exception cref="InvalidOperationException">BASS could not be initialized.</exception>
        public static void InitNoSoundDevice()
        {
            if (Bass.Init(0, SampleRate, DeviceInitFlags.Default))
            {
                OwnsDevice = true;
                return;
            }
            if (Bass.LastError != Errors.Already)
            {
                throw new InvalidOperationException($"BASS could not be initialized: {Bass.LastError}");
            }
            Bass.CurrentDevice = 0;
        }

        /// <summary>Frees BASS when this helper initialized it.</summary>
        public static void FreeNoSoundDevice()
        {
            if (OwnsDevice)
            {
                Bass.Free();
                OwnsDevice = false;
            }
        }

        /// <summary>Builds a 16-bit PCM WAV file with a constant-amplitude square wave.</summary>
        /// <param name="channels">The number of interleaved channels.</param>
        /// <param name="frames">The number of frames.</param>
        /// <param name="amplitude">The peak amplitude from 0 to 1.</param>
        /// <returns>The WAV file bytes.</returns>
        public static byte[] CreateSquareWave(int channels, int frames, float amplitude)
        {
            var samples = new short[frames * channels];
            var peak = (short)(amplitude * short.MaxValue);
            for (var frame = 0; frame < frames; frame++)
            {
                var value = (frame / 50) % 2 == 0 ? peak : (short)-peak;
                for (var channel = 0; channel < channels; channel++)
                {
                    samples[(frame * channels) + channel] = value;
                }
            }
            return CreateWav(channels, samples);
        }

        /// <summary>Builds a 16-bit PCM WAV file from interleaved samples.</summary>
        /// <param name="channels">The number of interleaved channels.</param>
        /// <param name="samples">The interleaved samples.</param>
        /// <returns>The WAV file bytes.</returns>
        public static byte[] CreateWav(int channels, short[] samples)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.ASCII);
            var dataLength = samples.Length * sizeof(short);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(SampleRate);
            writer.Write(SampleRate * channels * sizeof(short));
            writer.Write((short)(channels * sizeof(short)));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);
            foreach (var sample in samples)
            {
                writer.Write(sample);
            }
            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>Creates a decoding BASS stream over WAV bytes held in memory.</summary>
        /// <param name="wav">The WAV file bytes; BASS keeps reading them, so the caller must keep them alive.</param>
        /// <param name="flags">Additional stream flags.</param>
        /// <returns>The stream handle.</returns>
        /// <exception cref="InvalidOperationException">BASS could not create the stream.</exception>
        public static int CreateDecodeStream(byte[] wav, BassFlags flags = BassFlags.Default)
        {
            var handle = Bass.CreateStream(wav, 0, wav.Length, BassFlags.Decode | flags);
            if (handle == 0)
            {
                throw new InvalidOperationException($"BASS could not decode the test WAV: {Bass.LastError}");
            }
            return handle;
        }
    }
}
