using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Mix;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.ThirdParty.ManagedBass
{
    /// <summary>
    /// Verifies that the ManagedBass wrappers route native callbacks to any managed delegate, including capturing lambdas that
    /// IL2CPP cannot marshal, through their static trampolines, pass the caller's user value through, and release each callback
    /// with the channel, DSP or sync it belongs to.
    /// </summary>
    /// <remarks>
    /// The tests pull decoding channels on the "no sound" device, so BASS runs the callbacks synchronously on the test thread.
    /// They run on Mono in the Editor; that IL2CPP accepts the trampolines is covered by <see cref="NativeCallbackRulesTests"/>.
    /// </remarks>
    public class CallbackTrampolineTests
    {
        /// <summary>A user value that no wrapper produces itself.</summary>
        private static readonly IntPtr CallerUser = new IntPtr(0x5A17);

        /// <summary>The channels created by the test, freed in <see cref="TearDown"/>.</summary>
        private readonly List<int> _channels = new();

        /// <summary>The number of registered callback contexts before the test.</summary>
        private int _contextsBefore;

        /// <summary>Initializes BASS on the silent device once for the fixture.</summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            NoSoundDevice.Init();
        }

        /// <summary>Frees BASS after the fixture.</summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            NoSoundDevice.Free();
        }

        /// <summary>Records the number of registered callback contexts.</summary>
        [SetUp]
        public void SetUp()
        {
            _contextsBefore = CallbackContext.RegisteredCount;
        }

        /// <summary>Frees the channels of the test and checks that none of their callbacks stays registered.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (var channel in _channels)
            {
                Bass.StreamFree(channel);
            }
            _channels.Clear();
            Assert.That(CallbackContext.RegisteredCount, Is.EqualTo(_contextsBefore), "A callback context outlived its channel.");
        }

        /// <summary>Checks that a capturing stream procedure is called with the caller's user value.</summary>
        [Test]
        public void CreateStream_CapturingProcedure_ReceivesCallerUserValue()
        {
            var users = new List<IntPtr>();
            var stream = Track(Bass.CreateStream(NoSoundDevice.SampleRate, NoSoundDevice.Channels, BassFlags.Decode, (handle, buffer, length, user) =>
            {
                users.Add(user);
                return length;
            }, CallerUser));

            Assert.That(NoSoundDevice.Pull(stream, NoSoundDevice.BytesPerSecond), Is.EqualTo(NoSoundDevice.BytesPerSecond));
            Assert.That(users, Is.Not.Empty);
            Assert.That(users, Is.All.EqualTo(CallerUser));
            Assert.That(CallbackContext.RegisteredCount, Is.EqualTo(_contextsBefore + 1));
        }

        /// <summary>Checks that a stream created without a procedure is a dummy stream that registers no callback.</summary>
        [Test]
        public void CreateStream_NullProcedure_CreatesDummyStreamWithoutCallback()
        {
            var stream = Track(Bass.CreateStream(NoSoundDevice.SampleRate, NoSoundDevice.Channels, BassFlags.Decode, (StreamProcedure)null!));

            Assert.That(stream, Is.Not.Zero, () => Bass.LastError.ToString());
            Assert.That(CallbackContext.RegisteredCount, Is.EqualTo(_contextsBefore));
        }

        /// <summary>Checks that a rejected stream procedure does not stay registered.</summary>
        [Test]
        public void CreateStream_Rejected_ReleasesCallback()
        {
            var stream = Bass.CreateStream(NoSoundDevice.SampleRate, 0, BassFlags.Decode, (handle, buffer, length, user) => length);

            Assert.That(stream, Is.Zero);
            Assert.That(CallbackContext.RegisteredCount, Is.EqualTo(_contextsBefore));
        }

        /// <summary>Checks that freeing a channel releases its stream procedure, DSP and syncs.</summary>
        [Test]
        public void StreamFree_ReleasesEveryCallbackOfTheChannel()
        {
            var stream = CreateEndlessStream();
            Bass.ChannelSetDSP(stream, (handle, channel, buffer, length, user) => { });
            Bass.ChannelSetSync(stream, SyncFlags.End, 0, (handle, channel, data, user) => { });
            Bass.ChannelSetSync(stream, SyncFlags.Position | SyncFlags.Onetime, NoSoundDevice.BytesPerSecond, (handle, channel, data, user) => { });
            Assert.That(CallbackContext.RegisteredCount, Is.EqualTo(_contextsBefore + 4));

            Assert.That(Bass.StreamFree(stream), Is.True);

            Assert.That(CallbackContext.RegisteredCount, Is.EqualTo(_contextsBefore));
        }

        /// <summary>Checks that a capturing DSP is called with the caller's user value until it is removed.</summary>
        [Test]
        public void ChannelSetDSP_CapturingProcedure_IsCalledUntilRemoved()
        {
            var stream = Track(CreateEndlessStream());
            var users = new List<IntPtr>();
            var dsp = Bass.ChannelSetDSP(stream, (handle, channel, buffer, length, user) => users.Add(user), CallerUser);
            Assert.That(dsp, Is.Not.Zero, () => Bass.LastError.ToString());

            NoSoundDevice.Pull(stream, NoSoundDevice.BytesPerSecond);
            Assert.That(users, Is.Not.Empty);
            Assert.That(users, Is.All.EqualTo(CallerUser));

            Assert.That(Bass.ChannelRemoveDSP(stream, dsp), Is.True);
            Assert.That(CallbackContext.RegisteredCount, Is.EqualTo(_contextsBefore + 1));
            var callsBeforePull = users.Count;
            NoSoundDevice.Pull(stream, NoSoundDevice.BytesPerSecond);
            Assert.That(users.Count, Is.EqualTo(callsBeforePull));
        }

        /// <summary>Checks that a one-time sync fires once and is released after it fired.</summary>
        [Test]
        public void ChannelSetSync_OneTime_FiresOnceAndIsReleased()
        {
            var stream = Track(CreateEndlessStream());
            var users = new List<IntPtr>();
            var sync = Bass.ChannelSetSync(stream, SyncFlags.Position | SyncFlags.Onetime, NoSoundDevice.BytesPerSecond / 10, (handle, channel, data, user) => users.Add(user), CallerUser);
            Assert.That(sync, Is.Not.Zero, () => Bass.LastError.ToString());
            Assert.That(CallbackContext.RegisteredCount, Is.EqualTo(_contextsBefore + 2));

            NoSoundDevice.Pull(stream, NoSoundDevice.BytesPerSecond);

            Assert.That(users, Is.EqualTo(new[] { CallerUser }));
            Assert.That(CallbackContext.RegisteredCount, Is.EqualTo(_contextsBefore + 1));
        }

        /// <summary>
        /// Checks that a sync without <see cref="SyncFlags.Onetime"/> keeps firing and stays registered. A flag check that read past
        /// the enum value used to report <see cref="SyncFlags.Onetime"/> for any sync and released it after the first call.
        /// </summary>
        [Test]
        public void ChannelSetSync_Repeating_FiresEveryTimeAndStaysRegistered()
        {
            var wav = NoSoundDevice.CreateWav(NoSoundDevice.CreatePcm(NoSoundDevice.SampleRate / 10));
            var stream = Track(Bass.CreateStream(wav, 0, wav.Length, BassFlags.Decode));
            var calls = 0;
            Assert.That(Bass.ChannelSetSync(stream, SyncFlags.End, 0, (handle, channel, data, user) => calls++), Is.Not.Zero);

            NoSoundDevice.ReadToEnd(stream);
            Assert.That(Bass.ChannelSetPosition(stream, 0), Is.True);
            NoSoundDevice.ReadToEnd(stream);

            Assert.That(calls, Is.EqualTo(2));
            Assert.That(CallbackContext.RegisteredCount, Is.EqualTo(_contextsBefore + 1));
        }

        /// <summary>
        /// Checks that a free sync fires although it is set on a channel without other references: BASS calls the free syncs of a
        /// channel in reverse order, so the free sync that releases the channel's callbacks runs before it.
        /// </summary>
        [Test]
        public void ChannelSetSync_Free_FiresAfterTheChannelCallbacksWereReleased()
        {
            var stream = Bass.CreateStream(NoSoundDevice.SampleRate, NoSoundDevice.Channels, BassFlags.Decode, StreamProcedureType.Push);
            var freed = new List<int>();
            Assert.That(Bass.ChannelSetSync(stream, SyncFlags.Free, 0, (handle, channel, data, user) => freed.Add(channel)), Is.Not.Zero);
            Assert.That(Bass.ChannelSetSync(stream, SyncFlags.End, 0, (handle, channel, data, user) => { }), Is.Not.Zero);

            Assert.That(Bass.StreamFree(stream), Is.True);

            Assert.That(freed, Is.EqualTo(new[] { stream }));
        }

        /// <summary>Checks that removing a sync releases it.</summary>
        [Test]
        public void ChannelRemoveSync_ReleasesCallback()
        {
            var stream = Track(CreateEndlessStream());
            var sync = Bass.ChannelSetSync(stream, SyncFlags.Free, 0, (handle, channel, data, user) => { });

            Assert.That(Bass.ChannelRemoveSync(stream, sync), Is.True);

            Assert.That(CallbackContext.RegisteredCount, Is.EqualTo(_contextsBefore + 1));
        }

        /// <summary>
        /// Checks that a user file stream over a .NET stream decodes the whole file and closes the stream. Creating a stream without
        /// buffering seeks back after BASS probed the file, which failed while seeks were applied relative to the current position.
        /// </summary>
        [Test]
        public void CreateStream_StreamFileProcedures_DecodesAndClosesTheFile()
        {
            var pcm = NoSoundDevice.CreatePcm(NoSoundDevice.SampleRate / 2);
            var file = new MemoryStream(NoSoundDevice.CreateWav(pcm));

            var stream = Bass.CreateStream(StreamSystem.NoBuffer, BassFlags.Decode, Extensions.StreamFileProcedures(file));
            Assert.That(stream, Is.Not.Zero, () => Bass.LastError.ToString());

            Assert.That(NoSoundDevice.ReadToEnd(stream), Is.EqualTo(pcm));
            Assert.That(Bass.StreamFree(stream), Is.True);
            Assert.That(file.CanRead, Is.False, "BASS did not close the file.");
        }

        /// <summary>Checks that capturing file procedures receive the caller's user value and are released once BASS closes the file.</summary>
        [Test]
        public void CreateStream_CapturingFileProcedures_ReceiveCallerUserValue()
        {
            var file = new MemoryStream(NoSoundDevice.CreateWav(NoSoundDevice.CreatePcm(NoSoundDevice.SampleRate / 10)));
            var users = new List<IntPtr>();
            var closes = 0;
            var procedures = new FileProcedures
            {
                Close = user =>
                {
                    users.Add(user);
                    closes++;
                },
                Length = user =>
                {
                    users.Add(user);
                    return file.Length;
                },
                Read = (buffer, length, user) =>
                {
                    users.Add(user);
                    var bytes = new byte[length];
                    var read = file.Read(bytes, 0, length);
                    Marshal.Copy(bytes, 0, buffer, read);
                    return read;
                },
                Seek = (offset, user) =>
                {
                    users.Add(user);
                    file.Position = offset;
                    return true;
                }
            };

            var stream = Bass.CreateStream(StreamSystem.NoBuffer, BassFlags.Decode, procedures, CallerUser);
            Assert.That(stream, Is.Not.Zero, () => Bass.LastError.ToString());
            NoSoundDevice.ReadToEnd(stream);
            Assert.That(Bass.StreamFree(stream), Is.True);

            Assert.That(closes, Is.EqualTo(1));
            Assert.That(users, Is.Not.Empty);
            Assert.That(users, Is.All.EqualTo(CallerUser));
        }

        /// <summary>Checks that file procedures of a file BASS cannot open do not stay registered.</summary>
        [Test]
        public void CreateStream_RejectedFile_ReleasesFileProcedures()
        {
            var file = new MemoryStream(new byte[1000]);

            var stream = Bass.CreateStream(StreamSystem.NoBuffer, BassFlags.Decode, Extensions.StreamFileProcedures(file));

            Assert.That(stream, Is.Zero);
            Assert.That(Bass.LastError, Is.EqualTo(Errors.FileFormat));
        }

        /// <summary>Checks that a stream created from a managed array decodes it.</summary>
        [Test]
        public void CreateStream_ManagedMemory_DecodesTheMemory()
        {
            var pcm = NoSoundDevice.CreatePcm(NoSoundDevice.SampleRate / 10);
            var wav = NoSoundDevice.CreateWav(pcm);

            var stream = Track(Bass.CreateStream(wav, 0, wav.Length, BassFlags.Decode));
            Assert.That(stream, Is.Not.Zero, () => Bass.LastError.ToString());
            GC.Collect();
            GC.WaitForPendingFinalizers();

            Assert.That(NoSoundDevice.ReadToEnd(stream), Is.EqualTo(pcm));
        }

        /// <summary>Checks that <see cref="Extensions.SilenceStreamProcedure"/> writes silence.</summary>
        [Test]
        public void SilenceStreamProcedure_WritesSilence()
        {
            var stream = Track(Bass.CreateStream(NoSoundDevice.SampleRate, NoSoundDevice.Channels, BassFlags.Decode, Extensions.SilenceStreamProcedure));
            var buffer = new byte[4096];
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] = 0xFF;
            }

            Assert.That(Bass.ChannelGetData(stream, buffer, buffer.Length), Is.EqualTo(buffer.Length));

            Assert.That(buffer, Is.All.EqualTo((byte)0));
        }

        /// <summary>
        /// Checks that capturing mixer source syncs fire with the caller's user value and are released with their source.
        /// The <see cref="SyncProcedureEx"/> overload is left out: the bundled BASSmix 2.4.12 crashes when its sync type flag is
        /// set, also with a static callback passed by a plain P/Invoke.
        /// </summary>
        [Test]
        public void BassMixChannelSetSync_CapturingProcedures_FireAndAreReleasedWithTheSource()
        {
            var mixer = Track(BassMix.CreateMixerStream(NoSoundDevice.SampleRate, NoSoundDevice.Channels, BassFlags.Decode));
            Assert.That(mixer, Is.Not.Zero, () => Bass.LastError.ToString());
            var source = CreateEndlessStream();
            Assert.That(BassMix.MixerAddChannel(mixer, source, BassFlags.Default), Is.True, () => Bass.LastError.ToString());
            var oneTimeUsers = new List<IntPtr>();
            var repeatingUsers = new List<IntPtr>();
            var position = NoSoundDevice.BytesPerSecond / 10;
            Assert.That(BassMix.ChannelSetSync(source, SyncFlags.Position | SyncFlags.Onetime, position, (handle, channel, data, user) => oneTimeUsers.Add(user), CallerUser), Is.Not.Zero);
            Assert.That(BassMix.ChannelSetSync(source, SyncFlags.Position, position, (handle, channel, data, user) => repeatingUsers.Add(user), CallerUser), Is.Not.Zero);
            Assert.That(CallbackContext.RegisteredCount, Is.EqualTo(_contextsBefore + 3));

            NoSoundDevice.Pull(mixer, NoSoundDevice.BytesPerSecond);

            Assert.That(oneTimeUsers, Is.EqualTo(new[] { CallerUser }));
            Assert.That(repeatingUsers, Is.EqualTo(new[] { CallerUser }));
            Assert.That(CallbackContext.RegisteredCount, Is.EqualTo(_contextsBefore + 2), "The one-time mixer sync was not released.");
            Assert.That(Bass.StreamFree(source), Is.True);
            Assert.That(CallbackContext.RegisteredCount, Is.EqualTo(_contextsBefore));
        }

        /// <summary>Checks that <see cref="ChannelReferences"/> accepts arbitrary objects, replaced and removed more than once, alongside the wrappers' callbacks.</summary>
        [Test]
        public void ChannelReferences_ArbitraryObjects_CanBeReplacedAndRemoved()
        {
            var stream = Track(Bass.CreateStream(NoSoundDevice.SampleRate, NoSoundDevice.Channels, BassFlags.Decode, StreamProcedureType.Push));
            var kept = new object();

            ChannelReferences.Add(stream, 1, kept);
            ChannelReferences.Add(stream, 1, kept);
            ChannelReferences.Remove(stream, 1);
            ChannelReferences.Remove(stream, 1);

            Assert.That(CallbackContext.RegisteredCount, Is.EqualTo(_contextsBefore));
        }

        /// <summary>Creates a decoding stream that never ends, with a capturing stream procedure that IL2CPP could not marshal directly.</summary>
        /// <returns>The stream handle.</returns>
        private int CreateEndlessStream()
        {
            var writes = 0L;
            var stream = Bass.CreateStream(NoSoundDevice.SampleRate, NoSoundDevice.Channels, BassFlags.Decode, (handle, buffer, length, user) =>
            {
                writes += length;
                return length;
            });
            Assert.That(stream, Is.Not.Zero, () => Bass.LastError.ToString());
            return stream;
        }

        /// <summary>Remembers a channel so that <see cref="TearDown"/> frees it.</summary>
        /// <param name="channel">The channel handle.</param>
        /// <returns><paramref name="channel"/>.</returns>
        private int Track(int channel)
        {
            if (channel != 0)
            {
                _channels.Add(channel);
            }
            return channel;
        }
    }
}
