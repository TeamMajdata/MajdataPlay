using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagedBass;
using ManagedBass.Aac;
using ManagedBass.Mix;
using ManagedBass.Opus;

#nullable enable

/// <summary>
/// Runs ManagedBass, built as separate assemblies with the names of its NuGet packages, against the real Windows x64 BASS
/// libraries on CoreCLR, outside Unity. Every check uses capturing lambdas that BASS reaches through the static trampolines
/// of the wrappers, and verifies that their callback contexts are released with the channel, DSP or sync they belong to.
/// </summary>
internal static class Program
{
    /// <summary>The sample rate of the test audio.</summary>
    private const int SampleRate = 44100;

    /// <summary>The number of interleaved channels of the test audio.</summary>
    private const int ChannelCount = 2;

    /// <summary>The number of bytes in one second of 16-bit test audio.</summary>
    private const int BytesPerSecond = SampleRate * ChannelCount * sizeof(short);

    /// <summary>A user value that no wrapper produces itself.</summary>
    private static readonly IntPtr s_callerUser = new IntPtr(0x5A17);

    /// <summary>Reads the number of registered callback contexts, which ManagedBass keeps internal.</summary>
    private static readonly Func<int> s_registeredContexts = CreateRegisteredContextsReader();

    /// <summary>The checks by name.</summary>
    private static readonly (string Name, Action Check)[] s_checks =
    {
        (nameof(StreamProcedureReceivesCallerUserValue), StreamProcedureReceivesCallerUserValue),
        (nameof(NullStreamProcedureCreatesDummyStream), NullStreamProcedureCreatesDummyStream),
        (nameof(DspIsCalledUntilRemoved), DspIsCalledUntilRemoved),
        (nameof(OneTimeSyncFiresOnce), OneTimeSyncFiresOnce),
        (nameof(RepeatingSyncKeepsFiring), RepeatingSyncKeepsFiring),
        (nameof(FreeSyncFiresOnChannelWithoutOtherReferences), FreeSyncFiresOnChannelWithoutOtherReferences),
        (nameof(StreamFileProceduresDecodeAndCloseFile), StreamFileProceduresDecodeAndCloseFile),
        (nameof(RejectedFileReleasesFileProcedures), RejectedFileReleasesFileProcedures),
        (nameof(AddOnFileStreamsReleaseFileProcedures), AddOnFileStreamsReleaseFileProcedures),
        (nameof(MixerSourceSyncsFireAndAreReleased), MixerSourceSyncsFireAndAreReleased),
        (nameof(CollectedCallersKeepCallbacksAlive), CollectedCallersKeepCallbacksAlive),
        (nameof(ConcurrentChannelsReleaseAllCallbacks), ConcurrentChannelsReleaseAllCallbacks),
        (nameof(HasReadsOnlyTheEnumBytes), HasReadsOnlyTheEnumBytes),
    };

    /// <summary>The number of assertions that passed.</summary>
    private static int s_assertions;

    /// <summary>Runs every check, or only the checks named on the command line.</summary>
    /// <param name="args">Optional check names to run.</param>
    /// <returns>0 when every selected check passed; otherwise 1.</returns>
    private static int Main(string[] args)
    {
        // Device 0 is BASS's "no sound" device, so only decoding channels are created and the test pulls them itself.
        if (!Bass.Init(0, SampleRate))
        {
            Console.WriteLine($"MANAGEDBASS_VALIDATION_FAILED (Bass.Init failed: {Bass.LastError})");
            return 1;
        }
        Console.WriteLine($"BASS {Bass.Version}, BASSmix {BassMix.Version}, {RuntimeInformation.FrameworkDescription}, {typeof(Bass).Assembly.GetName().Name} {GetTargetFramework(typeof(Bass).Assembly)}");
        var failures = 0;
        try
        {
            foreach (var (name, check) in s_checks)
            {
                if (args.Length != 0 && !args.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                var contextsBefore = s_registeredContexts();
                var elapsed = Stopwatch.StartNew();
                try
                {
                    check();
                    Ensure(s_registeredContexts() == contextsBefore, $"{s_registeredContexts() - contextsBefore} callback contexts outlived their channels");
                    Console.WriteLine($"PASS {name} ({elapsed.ElapsedMilliseconds} ms)");
                }
                catch (Exception e)
                {
                    failures++;
                    Console.WriteLine($"FAIL {name}: {e}");
                }
            }
        }
        finally
        {
            Bass.Free();
        }
        Console.WriteLine(failures == 0
            ? $"MANAGEDBASS_VALIDATION_PASSED ({s_assertions} assertions)"
            : $"MANAGEDBASS_VALIDATION_FAILED ({failures} checks failed)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Checks that a capturing stream procedure receives the caller's user value and is released with its stream.</summary>
    private static void StreamProcedureReceivesCallerUserValue()
    {
        var users = new List<IntPtr>();
        var stream = Bass.CreateStream(SampleRate, ChannelCount, BassFlags.Decode, (handle, buffer, length, user) =>
        {
            users.Add(user);
            return length;
        }, s_callerUser);
        Ensure(stream != 0, $"CreateStream failed: {Bass.LastError}");
        Ensure(Pull(stream, BytesPerSecond) == BytesPerSecond, "the stream returned less data than requested");
        Ensure(users.Count > 0 && users.All(user => user == s_callerUser), "the procedure did not receive the caller's user value");
        Ensure(Bass.StreamFree(stream), $"StreamFree failed: {Bass.LastError}");
    }

    /// <summary>Checks that a stream without a procedure is a dummy stream that registers no callback.</summary>
    private static void NullStreamProcedureCreatesDummyStream()
    {
        var contextsBefore = s_registeredContexts();
        var stream = Bass.CreateStream(SampleRate, ChannelCount, BassFlags.Decode, (StreamProcedure)null!);
        Ensure(stream != 0, $"CreateStream failed: {Bass.LastError}");
        Ensure(s_registeredContexts() == contextsBefore, "a dummy stream registered a callback");
        Ensure(Bass.StreamFree(stream), $"StreamFree failed: {Bass.LastError}");
    }

    /// <summary>Checks that a capturing DSP receives the caller's user value until it is removed.</summary>
    private static void DspIsCalledUntilRemoved()
    {
        var stream = CreateEndlessStream();
        var users = new List<IntPtr>();
        var dsp = Bass.ChannelSetDSP(stream, (handle, channel, buffer, length, user) => users.Add(user), s_callerUser);
        Ensure(dsp != 0, $"ChannelSetDSP failed: {Bass.LastError}");
        Pull(stream, BytesPerSecond);
        Ensure(users.Count > 0 && users.All(user => user == s_callerUser), "the DSP did not receive the caller's user value");
        var contexts = s_registeredContexts();
        Ensure(Bass.ChannelRemoveDSP(stream, dsp), $"ChannelRemoveDSP failed: {Bass.LastError}");
        Ensure(s_registeredContexts() == contexts - 1, "removing the DSP did not release it");
        var calls = users.Count;
        Pull(stream, BytesPerSecond);
        Ensure(users.Count == calls, "a removed DSP was called");
        Ensure(Bass.StreamFree(stream), $"StreamFree failed: {Bass.LastError}");
    }

    /// <summary>Checks that a one-time sync fires once and is released after it fired.</summary>
    private static void OneTimeSyncFiresOnce()
    {
        var stream = CreateEndlessStream();
        var users = new List<IntPtr>();
        var contexts = s_registeredContexts();
        var sync = Bass.ChannelSetSync(stream, SyncFlags.Position | SyncFlags.Onetime, BytesPerSecond / 10, (handle, channel, data, user) => users.Add(user), s_callerUser);
        Ensure(sync != 0, $"ChannelSetSync failed: {Bass.LastError}");
        Pull(stream, BytesPerSecond);
        Ensure(users.Count == 1 && users[0] == s_callerUser, $"the one-time sync fired {users.Count} times");
        Ensure(s_registeredContexts() == contexts, "the one-time sync was not released after it fired");
        Ensure(Bass.StreamFree(stream), $"StreamFree failed: {Bass.LastError}");
    }

    /// <summary>Checks that a sync without <see cref="SyncFlags.Onetime"/> keeps firing and stays registered.</summary>
    private static void RepeatingSyncKeepsFiring()
    {
        var wav = CreateWav(CreatePcm(SampleRate / 10));
        var stream = Bass.CreateStream(wav, 0, wav.Length, BassFlags.Decode);
        Ensure(stream != 0, $"CreateStream failed: {Bass.LastError}");
        var calls = 0;
        var contexts = s_registeredContexts();
        Ensure(Bass.ChannelSetSync(stream, SyncFlags.End, 0, (handle, channel, data, user) => calls++) != 0, $"ChannelSetSync failed: {Bass.LastError}");
        ReadToEnd(stream);
        Ensure(Bass.ChannelSetPosition(stream, 0), $"ChannelSetPosition failed: {Bass.LastError}");
        ReadToEnd(stream);
        Ensure(calls == 2, $"the end sync fired {calls} times instead of twice");
        Ensure(s_registeredContexts() == contexts + 1, "the repeating sync was released after it fired");
        Ensure(Bass.StreamFree(stream), $"StreamFree failed: {Bass.LastError}");
    }

    /// <summary>
    /// Checks that a free sync fires on a channel without other references, whose free sync for the reference cleanup is set
    /// after it and therefore runs first: BASS calls the free syncs of a channel in reverse order.
    /// </summary>
    private static void FreeSyncFiresOnChannelWithoutOtherReferences()
    {
        var stream = Bass.CreateStream(SampleRate, ChannelCount, BassFlags.Decode, StreamProcedureType.Push);
        Ensure(stream != 0, $"CreateStream failed: {Bass.LastError}");
        var freed = new List<int>();
        Ensure(Bass.ChannelSetSync(stream, SyncFlags.Free, 0, (handle, channel, data, user) => freed.Add(channel)) != 0, $"ChannelSetSync failed: {Bass.LastError}");
        Ensure(Bass.ChannelSetSync(stream, SyncFlags.End, 0, (handle, channel, data, user) => { }) != 0, $"ChannelSetSync failed: {Bass.LastError}");
        Ensure(Bass.StreamFree(stream), $"StreamFree failed: {Bass.LastError}");
        Ensure(freed.Count == 1 && freed[0] == stream, $"the free sync fired {freed.Count} times");
    }

    /// <summary>
    /// Checks that a user file stream over a .NET stream decodes the whole file and closes the stream. Creating a stream without
    /// buffering seeks back after probing the file, which failed while seeks were applied relative to the current position.
    /// </summary>
    private static void StreamFileProceduresDecodeAndCloseFile()
    {
        var pcm = CreatePcm(SampleRate / 2);
        var file = new MemoryStream(CreateWav(pcm));
        var stream = Bass.CreateStream(StreamSystem.NoBuffer, BassFlags.Decode, Extensions.StreamFileProcedures(file), s_callerUser);
        Ensure(stream != 0, $"CreateStream failed: {Bass.LastError}");
        Ensure(ReadToEnd(stream).AsSpan().SequenceEqual(pcm), "the decoded samples differ from the file");
        Ensure(Bass.StreamFree(stream), $"StreamFree failed: {Bass.LastError}");
        Ensure(!file.CanRead, "BASS did not close the file");
    }

    /// <summary>Checks that file procedures of a file BASS cannot open are released.</summary>
    private static void RejectedFileReleasesFileProcedures()
    {
        var stream = Bass.CreateStream(StreamSystem.NoBuffer, BassFlags.Decode, Extensions.StreamFileProcedures(new MemoryStream(new byte[1000])));
        Ensure(stream == 0 && Bass.LastError == Errors.FileFormat, $"BASS accepted an empty file ({stream}, {Bass.LastError})");
    }

    /// <summary>
    /// Checks the file stream wrappers of the AAC and Opus add-ons, separate assemblies that use the core trampolines through
    /// InternalsVisibleTo: they reject a WAV file, close it and release its procedures.
    /// </summary>
    private static void AddOnFileStreamsReleaseFileProcedures()
    {
        var wav = CreateWav(CreatePcm(SampleRate / 10));
        var creators = new (string Name, Func<FileProcedures, int> Create)[]
        {
            ("BassAac.CreateStream", procedures => BassAac.CreateStream(StreamSystem.NoBuffer, BassFlags.Decode, procedures)),
            ("BassAac.CreateMp4Stream", procedures => BassAac.CreateMp4Stream(StreamSystem.NoBuffer, BassFlags.Decode, procedures)),
            ("BassOpus.CreateStream", procedures => BassOpus.CreateStream(StreamSystem.NoBuffer, BassFlags.Decode, procedures)),
        };
        foreach (var (name, create) in creators)
        {
            var file = new MemoryStream(wav);
            var contexts = s_registeredContexts();
            var stream = create(Extensions.StreamFileProcedures(file));
            if (stream != 0)
            {
                Bass.StreamFree(stream);
            }
            Ensure(s_registeredContexts() == contexts, $"{name} left its file procedures registered");
            Ensure(!file.CanRead, $"{name} did not close the file");
        }
    }

    /// <summary>
    /// Checks that capturing mixer source syncs fire with the caller's user value and are released with their source.
    /// The <see cref="SyncProcedureEx"/> overload is left out: the bundled BASSmix 2.4.12 crashes when its sync type flag is set,
    /// also with a static callback passed by a plain P/Invoke.
    /// </summary>
    private static void MixerSourceSyncsFireAndAreReleased()
    {
        var mixer = BassMix.CreateMixerStream(SampleRate, ChannelCount, BassFlags.Decode);
        Ensure(mixer != 0, $"CreateMixerStream failed: {Bass.LastError}");
        var source = CreateEndlessStream();
        Ensure(BassMix.MixerAddChannel(mixer, source, BassFlags.Default), $"MixerAddChannel failed: {Bass.LastError}");
        var users = new List<IntPtr>();
        var contexts = s_registeredContexts();
        Ensure(BassMix.ChannelSetSync(source, SyncFlags.Position | SyncFlags.Onetime, BytesPerSecond / 10, (handle, channel, data, user) => users.Add(user), s_callerUser) != 0, $"BassMix.ChannelSetSync failed: {Bass.LastError}");
        Ensure(BassMix.ChannelSetSync(source, SyncFlags.Position, BytesPerSecond / 10, (handle, channel, data, user) => users.Add(user), s_callerUser) != 0, $"BassMix.ChannelSetSync failed: {Bass.LastError}");
        Pull(mixer, BytesPerSecond);
        Ensure(users.Count == 2 && users.All(user => user == s_callerUser), $"the mixer syncs fired {users.Count} times");
        Ensure(s_registeredContexts() == contexts + 1, "the one-time mixer sync was not released after it fired");
        Ensure(Bass.StreamFree(source), $"StreamFree failed: {Bass.LastError}");
        Ensure(Bass.StreamFree(mixer), $"StreamFree failed: {Bass.LastError}");
    }

    /// <summary>Checks that the wrappers keep a callback alive after the caller dropped every reference to it and the GC ran.</summary>
    private static void CollectedCallersKeepCallbacksAlive()
    {
        var calls = new StrongBox<int>();
        var stream = CreateCountingStream(calls);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Pull(stream, BytesPerSecond);
        Ensure(calls.Value > 0, "the stream procedure was not called after a garbage collection");
        Ensure(Bass.StreamFree(stream), $"StreamFree failed: {Bass.LastError}");
    }

    /// <summary>Checks that channels created, pulled and freed on several threads at once release every callback.</summary>
    private static void ConcurrentChannelsReleaseAllCallbacks()
    {
        const int Iterations = 300;
        var failures = 0;
        Parallel.For(0, Environment.ProcessorCount * 2, _ =>
        {
            for (var i = 0; i < Iterations; i++)
            {
                var stream = CreateEndlessStream();
                var dspCalls = 0;
                var syncCalls = 0;
                Bass.ChannelSetDSP(stream, (handle, channel, buffer, length, user) => dspCalls++);
                Bass.ChannelSetSync(stream, SyncFlags.Position | SyncFlags.Onetime, 4096, (handle, channel, data, user) => syncCalls++);
                Bass.ChannelSetSync(stream, SyncFlags.Free, 0, (handle, channel, data, user) => syncCalls++);
                Pull(stream, 16384);
                if (!Bass.StreamFree(stream) || dspCalls == 0 || syncCalls != 2)
                {
                    Interlocked.Increment(ref failures);
                }
            }
        });
        Ensure(failures == 0, $"{failures} concurrent channels missed callbacks or could not be freed");
    }

    /// <summary>
    /// Checks <see cref="EnumExtensions.Has{T}(T, T)"/> on four-byte enums after writing to the stack. It used to read eight
    /// bytes, so on optimized CoreCLR code nearly every call reported <see cref="SyncFlags.Onetime"/> for any sync.
    /// </summary>
    private static void HasReadsOnlyTheEnumBytes()
    {
        var falsePositives = 0;
        for (var i = 0; i < 1_000_000; i++)
        {
            FillStack(-1L, i);
            if (HasOnetime(SyncFlags.End))
            {
                falsePositives++;
            }
        }
        Ensure(falsePositives == 0, $"Has reported a missing flag {falsePositives} times");
        Ensure((SyncFlags.End | SyncFlags.Onetime).Has(SyncFlags.Onetime), "Has missed the sign bit");
        Ensure(!BassFlags.Float.Has(BassFlags.Float | BassFlags.Decode), "Has accepted a flag with only some of its bits set");
    }

    /// <summary>Calls <see cref="EnumExtensions.Has{T}(T, T)"/> in a frame of its own.</summary>
    /// <param name="type">The sync type to test.</param>
    /// <returns>Whether <paramref name="type"/> has <see cref="SyncFlags.Onetime"/>.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool HasOnetime(SyncFlags type)
    {
        return type.Has(SyncFlags.Onetime);
    }

    /// <summary>Uses a stack frame with all bits set, so later frames start on non-zero stack memory.</summary>
    /// <param name="value">A value with all bits set.</param>
    /// <param name="index">A value that varies between calls.</param>
    /// <returns>A value derived from the arguments, so the call is not removed.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long FillStack(long value, long index)
    {
        return (value ^ index) + (value * index);
    }

    /// <summary>Creates a decoding stream whose capturing procedure counts its calls, in a frame of its own so no local keeps the procedure alive.</summary>
    /// <param name="calls">Receives the number of calls.</param>
    /// <returns>The stream handle.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int CreateCountingStream(StrongBox<int> calls)
    {
        var stream = Bass.CreateStream(SampleRate, ChannelCount, BassFlags.Decode, (handle, buffer, length, user) =>
        {
            calls.Value++;
            return length;
        });
        Ensure(stream != 0, $"CreateStream failed: {Bass.LastError}");
        return stream;
    }

    /// <summary>Creates a decoding stream that never ends, with a capturing procedure.</summary>
    /// <returns>The stream handle.</returns>
    private static int CreateEndlessStream()
    {
        var written = 0L;
        var stream = Bass.CreateStream(SampleRate, ChannelCount, BassFlags.Decode, (handle, buffer, length, user) =>
        {
            written += length;
            return length;
        });
        Ensure(stream != 0, $"CreateStream failed: {Bass.LastError}");
        return stream;
    }

    /// <summary>Pulls sample data from a decoding channel, which runs its callbacks on the calling thread.</summary>
    /// <param name="handle">The decoding channel.</param>
    /// <param name="length">The number of bytes to pull.</param>
    /// <returns>The number of bytes BASS returned.</returns>
    private static int Pull(int handle, int length)
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

    /// <summary>Builds 16-bit stereo PCM samples that differ from frame to frame.</summary>
    /// <param name="frames">The number of frames.</param>
    /// <returns>The interleaved little-endian sample bytes.</returns>
    private static byte[] CreatePcm(int frames)
    {
        var pcm = new byte[frames * ChannelCount * sizeof(short)];
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
    private static byte[] CreateWav(byte[] pcm)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + pcm.Length);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)ChannelCount);
        writer.Write(SampleRate);
        writer.Write(BytesPerSecond);
        writer.Write((short)(ChannelCount * sizeof(short)));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(pcm.Length);
        writer.Write(pcm);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Creates a reader for the internal <c>CallbackContext.RegisteredCount</c> of the ManagedBass assembly.</summary>
    /// <returns>A function that returns the number of registered callback contexts.</returns>
    /// <exception cref="MissingMemberException">The ManagedBass assembly has no such property.</exception>
    private static Func<int> CreateRegisteredContextsReader()
    {
        var property = typeof(Bass).Assembly.GetType("ManagedBass.CallbackContext")?.GetProperty("RegisteredCount", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMemberException("ManagedBass.CallbackContext", "RegisteredCount");
        return () => (int)property.GetValue(null)!;
    }

    /// <summary>Gets the target framework an assembly was built for.</summary>
    /// <param name="assembly">The assembly.</param>
    /// <returns>The target framework name, or "unknown".</returns>
    private static string GetTargetFramework(Assembly assembly)
    {
        return assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName ?? "unknown";
    }

    /// <summary>Counts a passed assertion or throws when it failed.</summary>
    /// <param name="condition">The asserted condition.</param>
    /// <param name="message">The failure description.</param>
    /// <exception cref="InvalidOperationException"><paramref name="condition"/> is false.</exception>
    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
        Interlocked.Increment(ref s_assertions);
    }
}
