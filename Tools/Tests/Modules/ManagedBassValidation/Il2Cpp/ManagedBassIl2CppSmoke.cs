using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ManagedBass;
using ManagedBass.Mix;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Validation.Il2Cpp
{
    /// <summary>
    /// Runs the ManagedBass callback wrappers in a Player with capturing lambdas, which IL2CPP refuses to marshal to native code,
    /// and logs one <c>MANAGEDBASS_IL2CPP</c> line per check plus a final PASSED or FAILED line.
    /// </summary>
    public static class ManagedBassIl2CppSmoke
    {
        /// <summary>The sample rate of the test audio.</summary>
        private const int SampleRate = 44100;

        /// <summary>The number of bytes in one second of 16-bit stereo test audio.</summary>
        private const int BytesPerSecond = SampleRate * 2 * sizeof(short);

        /// <summary>The prefix of every result line.</summary>
        private const string Prefix = "MANAGEDBASS_IL2CPP";

        /// <summary>A user value that no wrapper produces itself.</summary>
        private static readonly IntPtr s_callerUser = new IntPtr(0x5A17);

        /// <summary>The number of failed checks.</summary>
        private static int s_failures;

        /// <summary>Sets a sync on a channel with a delegate that is marshalled directly, as ManagedBass did before.</summary>
        /// <param name="handle">The channel handle.</param>
        /// <param name="type">The type of sync.</param>
        /// <param name="parameter">The sync parameter.</param>
        /// <param name="procedure">The callback, marshalled by the runtime.</param>
        /// <param name="user">The user value.</param>
        /// <returns>The sync handle, or 0.</returns>
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
#else
        [DllImport("bass")]
#endif
        private static extern int BASS_ChannelSetSync(int handle, SyncFlags type, long parameter, SyncProcedure procedure, IntPtr user);

        /// <summary>Runs every check after the first scene loaded and quits with exit code 0 when all of them passed.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            Log($"start: {Application.platform}, {IntPtr.Size * 8}-bit, IL2CPP={IsIl2Cpp}, IsDynamicCodeSupported={CrossPlatformHelper.IsDynamicCodeSupported}");
            if (!Bass.Init(0, SampleRate))
            {
                Fail("Bass.Init", Bass.LastError.ToString());
                Finish();
                return;
            }
            try
            {
                Check("DirectClosureIsRejected", DirectClosureIsRejected);
                Check("StreamProcedure", StreamProcedure);
                Check("Dsp", Dsp);
                Check("OneTimeSync", OneTimeSync);
                Check("RepeatingSyncOnManagedMemory", RepeatingSyncOnManagedMemory);
                Check("FreeSync", FreeSync);
                Check("StreamFileProcedures", StreamFileProcedures);
                Check("MixerSourceSync", MixerSourceSync);
                Check("SilenceStreamProcedure", SilenceStreamProcedure);
                Check("Has", Has);
            }
            finally
            {
                Bass.Free();
            }
            Finish();
        }

        /// <summary>Gets a value indicating whether the Player runs on IL2CPP.</summary>
        private static bool IsIl2Cpp
        {
            get
            {
#if ENABLE_IL2CPP
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Control: marshalling a capturing lambda directly throws <see cref="NotSupportedException"/> on IL2CPP, which is what
        /// ChannelReferences ran into. It shows that the Player enforces the rules the other checks rely on.
        /// </summary>
        private static void DirectClosureIsRejected()
        {
            var stream = Bass.CreateStream(SampleRate, 2, BassFlags.Decode, StreamProcedureType.Push);
            Ensure(stream != 0, $"CreateStream failed: {Bass.LastError}");
            var calls = 0;
            Exception? error = null;
            try
            {
                BASS_ChannelSetSync(stream, SyncFlags.End, 0, (handle, channel, data, user) => calls++, IntPtr.Zero);
            }
            catch (Exception e)
            {
                error = e;
            }
            Bass.StreamFree(stream);
            if (IsIl2Cpp)
            {
                Ensure(error is NotSupportedException, $"IL2CPP marshalled a capturing lambda ({error?.GetType().Name ?? "no exception"})");
            }
        }

        /// <summary>Checks that a capturing stream procedure receives the caller's user value.</summary>
        private static void StreamProcedure()
        {
            var users = new List<IntPtr>();
            var stream = Bass.CreateStream(SampleRate, 2, BassFlags.Decode | BassFlags.Float, (handle, buffer, length, user) =>
            {
                users.Add(user);
                return length;
            }, s_callerUser);
            Ensure(stream != 0, $"CreateStream failed: {Bass.LastError}");
            Pull(stream, BytesPerSecond);
            Ensure(users.Count > 0 && users.TrueForAll(user => user == s_callerUser), "the procedure did not receive the caller's user value");
            Ensure(Bass.StreamFree(stream), $"StreamFree failed: {Bass.LastError}");
        }

        /// <summary>Checks that a capturing DSP is called until it is removed.</summary>
        private static void Dsp()
        {
            var stream = CreateEndlessStream();
            var calls = 0;
            var dsp = Bass.ChannelSetDSP(stream, (handle, channel, buffer, length, user) => calls++, s_callerUser);
            Ensure(dsp != 0, $"ChannelSetDSP failed: {Bass.LastError}");
            Pull(stream, BytesPerSecond);
            Ensure(calls > 0, "the DSP was not called");
            Ensure(Bass.ChannelRemoveDSP(stream, dsp), $"ChannelRemoveDSP failed: {Bass.LastError}");
            var callsBeforePull = calls;
            Pull(stream, BytesPerSecond);
            Ensure(calls == callsBeforePull, "a removed DSP was called");
            Ensure(Bass.StreamFree(stream), $"StreamFree failed: {Bass.LastError}");
        }

        /// <summary>Checks that a capturing one-time sync fires once.</summary>
        private static void OneTimeSync()
        {
            var stream = CreateEndlessStream();
            var calls = 0;
            Ensure(Bass.ChannelSetSync(stream, SyncFlags.Position | SyncFlags.Onetime, BytesPerSecond / 10, (handle, channel, data, user) => calls++) != 0, $"ChannelSetSync failed: {Bass.LastError}");
            Pull(stream, BytesPerSecond);
            Ensure(calls == 1, $"the one-time sync fired {calls} times");
            Ensure(Bass.StreamFree(stream), $"StreamFree failed: {Bass.LastError}");
        }

        /// <summary>Checks a repeating sync on a stream from a managed array, which pins the array until a free sync unpins it.</summary>
        private static void RepeatingSyncOnManagedMemory()
        {
            var wav = CreateWav(SampleRate / 10);
            var stream = Bass.CreateStream(wav, 0, wav.Length, BassFlags.Decode);
            Ensure(stream != 0, $"CreateStream failed: {Bass.LastError}");
            var calls = 0;
            Ensure(Bass.ChannelSetSync(stream, SyncFlags.End, 0, (handle, channel, data, user) => calls++) != 0, $"ChannelSetSync failed: {Bass.LastError}");
            ReadToEnd(stream);
            Ensure(Bass.ChannelSetPosition(stream, 0), $"ChannelSetPosition failed: {Bass.LastError}");
            ReadToEnd(stream);
            Ensure(calls == 2, $"the end sync fired {calls} times");
            Ensure(Bass.StreamFree(stream), $"StreamFree failed: {Bass.LastError}");
        }

        /// <summary>Checks that a capturing free sync fires.</summary>
        private static void FreeSync()
        {
            var stream = Bass.CreateStream(SampleRate, 2, BassFlags.Decode, StreamProcedureType.Push);
            Ensure(stream != 0, $"CreateStream failed: {Bass.LastError}");
            var freed = 0;
            Ensure(Bass.ChannelSetSync(stream, SyncFlags.Free, 0, (handle, channel, data, user) => freed++) != 0, $"ChannelSetSync failed: {Bass.LastError}");
            Ensure(Bass.StreamFree(stream), $"StreamFree failed: {Bass.LastError}");
            Ensure(freed == 1, $"the free sync fired {freed} times");
        }

        /// <summary>Checks that a user file stream over a .NET stream decodes and closes the file.</summary>
        private static void StreamFileProcedures()
        {
            var wav = CreateWav(SampleRate / 2);
            var file = new MemoryStream(wav);
            var stream = Bass.CreateStream(StreamSystem.NoBuffer, BassFlags.Decode, Extensions.StreamFileProcedures(file));
            Ensure(stream != 0, $"CreateStream failed: {Bass.LastError}");
            Ensure(ReadToEnd(stream).Length == wav.Length - 44, "the decoded length differs from the file");
            Ensure(Bass.StreamFree(stream), $"StreamFree failed: {Bass.LastError}");
            Ensure(!file.CanRead, "BASS did not close the file");
        }

        /// <summary>Checks that a capturing mixer source sync fires.</summary>
        private static void MixerSourceSync()
        {
            var mixer = BassMix.CreateMixerStream(SampleRate, 2, BassFlags.Decode);
            Ensure(mixer != 0, $"CreateMixerStream failed: {Bass.LastError}");
            var source = CreateEndlessStream();
            Ensure(BassMix.MixerAddChannel(mixer, source, BassFlags.Default), $"MixerAddChannel failed: {Bass.LastError}");
            var calls = 0;
            Ensure(BassMix.ChannelSetSync(source, SyncFlags.Position | SyncFlags.Onetime, BytesPerSecond / 10, (handle, channel, data, user) => calls++) != 0, $"BassMix.ChannelSetSync failed: {Bass.LastError}");
            Pull(mixer, BytesPerSecond);
            Ensure(calls == 1, $"the mixer sync fired {calls} times");
            Ensure(Bass.StreamFree(source), $"StreamFree failed: {Bass.LastError}");
            Ensure(Bass.StreamFree(mixer), $"StreamFree failed: {Bass.LastError}");
        }

        /// <summary>Checks <see cref="Extensions.SilenceStreamProcedure"/>.</summary>
        private static void SilenceStreamProcedure()
        {
            var stream = Bass.CreateStream(SampleRate, 2, BassFlags.Decode, Extensions.SilenceStreamProcedure);
            Ensure(stream != 0, $"CreateStream failed: {Bass.LastError}");
            var buffer = new byte[4096];
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] = 0xFF;
            }
            Ensure(Bass.ChannelGetData(stream, buffer, buffer.Length) == buffer.Length, $"ChannelGetData failed: {Bass.LastError}");
            Ensure(Array.TrueForAll(buffer, value => value == 0), "the stream was not silent");
            Ensure(Bass.StreamFree(stream), $"StreamFree failed: {Bass.LastError}");
        }

        /// <summary>Checks <see cref="EnumExtensions.Has{T}(T, T)"/>.</summary>
        private static void Has()
        {
            var falsePositives = 0;
            for (var i = 0; i < 100_000; i++)
            {
                if (((SyncFlags)(i & 0xF)).Has(SyncFlags.Onetime))
                {
                    falsePositives++;
                }
            }
            Ensure(falsePositives == 0, $"Has reported a missing flag {falsePositives} times");
            Ensure((SyncFlags.End | SyncFlags.Onetime).Has(SyncFlags.Onetime), "Has missed the sign bit");
        }

        /// <summary>Creates a decoding stream that never ends, with a capturing procedure.</summary>
        /// <returns>The stream handle.</returns>
        private static int CreateEndlessStream()
        {
            var written = 0L;
            var stream = Bass.CreateStream(SampleRate, 2, BassFlags.Decode, (handle, buffer, length, user) =>
            {
                written += length;
                return length;
            });
            Ensure(stream != 0, $"CreateStream failed: {Bass.LastError}");
            return stream;
        }

        /// <summary>Pulls sample data from a decoding channel.</summary>
        /// <param name="handle">The decoding channel.</param>
        /// <param name="length">The number of bytes to pull.</param>
        private static void Pull(int handle, int length)
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
        }

        /// <summary>Decodes a decoding channel to its end.</summary>
        /// <param name="handle">The decoding channel.</param>
        /// <returns>The decoded bytes.</returns>
        private static byte[] ReadToEnd(int handle)
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

        /// <summary>Builds a 16-bit stereo WAV file.</summary>
        /// <param name="frames">The number of frames.</param>
        /// <returns>The WAV file bytes.</returns>
        private static byte[] CreateWav(int frames)
        {
            var dataLength = frames * 4;
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.ASCII);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)2);
            writer.Write(SampleRate);
            writer.Write(BytesPerSecond);
            writer.Write((short)4);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);
            for (var i = 0; i < frames * 2; i++)
            {
                writer.Write((short)((i * 37) % 20000 - 10000));
            }
            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>Runs a check and logs its result.</summary>
        /// <param name="name">The check name.</param>
        /// <param name="check">The check.</param>
        private static void Check(string name, Action check)
        {
            try
            {
                check();
                Log($"PASS {name}");
            }
            catch (Exception e)
            {
                Fail(name, e.ToString());
            }
        }

        /// <summary>Throws when an assertion failed.</summary>
        /// <param name="condition">The asserted condition.</param>
        /// <param name="message">The failure description.</param>
        /// <exception cref="InvalidOperationException"><paramref name="condition"/> is false.</exception>
        private static void Ensure(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>Logs a failed check.</summary>
        /// <param name="name">The check name.</param>
        /// <param name="detail">The failure description.</param>
        private static void Fail(string name, string detail)
        {
            s_failures++;
            Log($"FAIL {name}: {detail}");
        }

        /// <summary>Logs the final result and quits.</summary>
        private static void Finish()
        {
            Log(s_failures == 0 ? "PASSED" : $"FAILED ({s_failures} checks)");
            Application.Quit(s_failures == 0 ? 0 : 1);
        }

        /// <summary>Logs a result line.</summary>
        /// <param name="message">The message.</param>
        private static void Log(string message)
        {
            Debug.Log($"{Prefix} {message}");
        }
    }
}
