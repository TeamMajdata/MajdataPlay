using System;
using System.Collections.Generic;
using MajdataPlay.IO;
using ManagedBass;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>
    /// Verifies the capture DSP of <see cref="BassAudioRecorder"/> on decoding streams pulled by the test, so the DSP runs
    /// synchronously on the test thread and no audio device is needed.
    /// </summary>
    public class BassAudioRecorderTests
    {
        /// <summary>The number of frames pulled per block, a tenth of a second.</summary>
        private const int BlockFrames = 4410;

        /// <summary>The WAV data kept alive while BASS decodes it.</summary>
        private readonly List<byte[]> _wavs = new();

        /// <summary>The decoding streams created by the test.</summary>
        private readonly List<int> _streams = new();

        /// <summary>Initializes BASS on the silent device once for the fixture.</summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            BassTestSupport.InitNoSoundDevice();
        }

        /// <summary>Frees BASS after the fixture.</summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            BassTestSupport.FreeNoSoundDevice();
        }

        /// <summary>Frees the streams of the test.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (var stream in _streams)
            {
                Bass.StreamFree(stream);
            }
            _streams.Clear();
            _wavs.Clear();
        }

        /// <summary>Checks that the recorder validates its arguments and the channel format.</summary>
        [Test]
        public void Constructor_ValidatesArguments()
        {
            var stream = CreateStream(2, BassFlags.Float);
            var probe = new FakeLatencyProbe();

            Assert.Throws<ArgumentOutOfRangeException>(() => new BassAudioRecorder(stream, 0, probe));
            Assert.Throws<ArgumentNullException>(() => new BassAudioRecorder(stream, 1, null!));
            Assert.Throws<InvalidOperationException>(() => new BassAudioRecorder(0, 1, probe));
            var byteStream = Bass.CreateStream(BassTestSupport.SampleRate, 2, BassFlags.Byte | BassFlags.Decode, StreamProcedureType.Push);
            _streams.Add(byteStream);
            Assert.Throws<NotSupportedException>(() => new BassAudioRecorder(byteStream, 1, probe));
        }

        /// <summary>Checks that the recorder reports the channel layout and keeps at most a stereo pair.</summary>
        [Test]
        public void Layout_KeepsAtMostTwoChannels()
        {
            using var stereo = new BassAudioRecorder(CreateStream(2, BassFlags.Float), 1, new FakeLatencyProbe());
            using var mono = new BassAudioRecorder(CreateStream(1, BassFlags.Float), 1, new FakeLatencyProbe());
            using var quad = new BassAudioRecorder(CreateStream(4, BassFlags.Float), 1, new FakeLatencyProbe());

            Assert.That(stereo.SampleRate, Is.EqualTo(BassTestSupport.SampleRate));
            Assert.That(stereo.Channels, Is.EqualTo(2));
            Assert.That(mono.Channels, Is.EqualTo(1));
            Assert.That(quad.Channels, Is.EqualTo(2));
            Assert.That(stereo.IsRecording, Is.False);
            Assert.That(stereo.PlaybackDelay, Is.EqualTo(TimeSpan.Zero));
        }

        /// <summary>Checks that float output is captured verbatim only while recording.</summary>
        [Test]
        public void FloatCapture_RecordsOnlyWhileStarted()
        {
            var stream = CreateStream(2, BassFlags.Float);
            using var recorder = new BassAudioRecorder(stream, 1, new FakeLatencyProbe { Latencies = { 0.05 } });

            Pull(stream);
            Assert.That(recorder.Read(new float[64]), Is.EqualTo(0), "nothing is captured before Start");

            recorder.Start();
            recorder.Start();
            Assert.That(recorder.IsRecording, Is.True);
            var pulled = Pull(stream);
            var captured = new float[pulled.Length];
            var read = recorder.Read(captured);

            Assert.That(read, Is.EqualTo(pulled.Length));
            Assert.That(captured, Is.EqualTo(pulled));
            Assert.That(recorder.PlaybackDelay.TotalSeconds, Is.GreaterThanOrEqualTo(0.05));

            recorder.Stop();
            recorder.Stop();
            Pull(stream);
            Assert.That(recorder.IsRecording, Is.False);
            Assert.That(recorder.Read(captured), Is.EqualTo(0));
        }

        /// <summary>Checks that 16-bit output is converted to float samples.</summary>
        [Test]
        public void ShortCapture_ConvertsToFloat()
        {
            var stream = CreateStream(2, BassFlags.Default);
            using var recorder = new BassAudioRecorder(stream, 1, new FakeLatencyProbe());

            recorder.Start();
            var buffer = new short[BlockFrames * 2];
            var bytes = Bass.ChannelGetData(stream, buffer, buffer.Length * sizeof(short));
            var captured = new float[buffer.Length];
            var read = recorder.Read(captured);

            Assert.That(bytes, Is.EqualTo(buffer.Length * sizeof(short)));
            Assert.That(read, Is.EqualTo(buffer.Length));
            Assert.That(captured[0], Is.EqualTo(buffer[0] / 32768f).Within(1e-6f));
            Assert.That(captured[read - 1], Is.EqualTo(buffer[read - 1] / 32768f).Within(1e-6f));
        }

        /// <summary>Checks that a block heard much later than expected is preceded by silence and the gap settles afterwards.</summary>
        [Test]
        public void OutputGap_IsFilledWithSilence()
        {
            var stream = CreateStream(2, BassFlags.Float);
            var probe = new FakeLatencyProbe { Latencies = { 0, 1.0, 1.0, 0, 0 } };
            using var recorder = new BassAudioRecorder(stream, 3, probe);
            recorder.Start();

            for (var i = 0; i < 5; i++)
            {
                Pull(stream);
            }

            Assert.That(recorder.OutputGapCount, Is.EqualTo(1));
            Assert.That(recorder.InsertedSilence.TotalSeconds, Is.EqualTo(0.9).Within(0.2));
            Assert.That(recorder.LargestTimelineDeviation, Is.GreaterThan(TimeSpan.Zero));
            recorder.Stop();
            var all = new float[BassTestSupport.SampleRate * 2 * 3];
            var read = recorder.Read(all);
            Assert.That(read / 2, Is.EqualTo((5 * BlockFrames) + (int)(recorder.InsertedSilence.TotalSeconds * BassTestSupport.SampleRate)).Within(2));
        }

        /// <summary>Checks that the remaining lateness is filled once the output latency returns after the settle timeout.</summary>
        [Test]
        public void OutputGap_WithoutRecoveredLatency_SettlesAfterTimeout()
        {
            var stream = CreateStream(2, BassFlags.Float);
            var probe = new FakeLatencyProbe { Latencies = { 0, 0.5, 0.2 } };
            using var recorder = new BassAudioRecorder(stream, 3, probe);
            recorder.Start();
            Pull(stream);
            Pull(stream);
            PrivateMember.SetField(recorder, "_settleDeadlineSeconds", -1.0);

            Pull(stream);

            Assert.That(recorder.OutputGapCount, Is.EqualTo(1));
            Assert.That(PrivateMember.GetField<bool>(recorder, "_isSettling"), Is.False);
        }

        /// <summary>Checks that frames beyond the buffer capacity are counted as dropped.</summary>
        [Test]
        public void SlowConsumer_DropsFrames()
        {
            var stream = CreateStream(2, BassFlags.Float, BassTestSupport.SampleRate * 2);
            using var recorder = new BassAudioRecorder(stream, 1, new FakeLatencyProbe());
            recorder.Start();

            for (var i = 0; i < 15; i++)
            {
                Pull(stream);
            }

            Assert.That(recorder.DroppedFrames, Is.GreaterThan(0));
            recorder.Stop();
        }

        /// <summary>Checks that a failure inside the capture callback is contained and reported at Stop.</summary>
        [Test]
        public void ProbeFailure_IsContained()
        {
            var stream = CreateStream(2, BassFlags.Float);
            using var recorder = new BassAudioRecorder(stream, 1, new FakeLatencyProbe { Throw = true });
            recorder.Start();

            Assert.DoesNotThrow(() => Pull(stream));
            Assert.DoesNotThrow(() => recorder.Stop());
        }

        /// <summary>Checks that disposal removes the capture, disposes the probe and rejects further use.</summary>
        [Test]
        public void Dispose_ReleasesTheProbeAndRejectsUse()
        {
            var stream = CreateStream(2, BassFlags.Float);
            var probe = new FakeLatencyProbe();
            var recorder = new BassAudioRecorder(stream, 1, probe);
            recorder.Start();

            recorder.Dispose();
            recorder.Dispose();
            Pull(stream);

            Assert.That(probe.Disposed, Is.EqualTo(1));
            Assert.That(recorder.IsRecording, Is.False);
            Assert.Throws<ObjectDisposedException>(() => recorder.Start());
            Assert.Throws<ObjectDisposedException>(() => recorder.Stop());
            Assert.Throws<ObjectDisposedException>(() => recorder.Read(new float[2]));
        }

        /// <summary>Checks the device stream probe, which measures a silent stream on the same device.</summary>
        [Test]
        public void DeviceStreamProbe_MeasuresOrRejectsTheChannel()
        {
            Assert.Throws<InvalidOperationException>(() => BassOutputLatencyProbe.CreateForDeviceStream(0));

            using var probe = BassOutputLatencyProbe.CreateForDeviceStream(CreateStream(2, BassFlags.Float));
            var latency = probe.Measure(0.01);

            Assert.That(double.IsNaN(latency), Is.False);
            Assert.That(latency, Is.GreaterThanOrEqualTo(-1));
        }

        /// <summary>Checks the WASAPI and ASIO probes without initialized outputs.</summary>
        [Test]
        public void BackendProbes_WithoutOutput()
        {
            Assert.Throws<InvalidOperationException>(() => BassOutputLatencyProbe.CreateForWasapi());
            using var asio = BassOutputLatencyProbe.CreateForAsio();

            Assert.That(asio.Measure(0.01), Is.EqualTo(-1));
        }

        /// <summary>Creates a decoding stream over a square wave.</summary>
        /// <param name="channels">The number of channels.</param>
        /// <param name="flags">Additional flags selecting the sample format.</param>
        /// <param name="frames">The number of frames.</param>
        /// <returns>The stream handle.</returns>
        private int CreateStream(int channels, BassFlags flags, int frames = BassTestSupport.SampleRate)
        {
            var wav = BassTestSupport.CreateSquareWave(channels, frames, 0.25f);
            _wavs.Add(wav);
            var stream = BassTestSupport.CreateDecodeStream(wav, flags);
            _streams.Add(stream);
            return stream;
        }

        /// <summary>Pulls one block of float samples from a stereo stream, running its DSP chain.</summary>
        /// <param name="stream">The decoding stream.</param>
        /// <returns>The pulled samples.</returns>
        private static float[] Pull(int stream)
        {
            var buffer = new float[BlockFrames * 2];
            var bytes = Bass.ChannelGetData(stream, buffer, buffer.Length * sizeof(float));
            if (bytes <= 0)
            {
                return Array.Empty<float>();
            }
            var result = new float[bytes / sizeof(float)];
            Array.Copy(buffer, result, result.Length);
            return result;
        }

        /// <summary>A latency probe returning scripted measurements.</summary>
        private sealed class FakeLatencyProbe : BassOutputLatencyProbe
        {
            /// <summary>The index of the next scripted measurement.</summary>
            private int _next;

            /// <summary>Gets the measurements returned in order; the last one repeats, and none means "unknown".</summary>
            public List<double> Latencies { get; } = new();

            /// <summary>Gets or sets a value indicating whether measuring throws.</summary>
            public bool Throw { get; set; }

            /// <summary>Gets the number of disposals.</summary>
            public int Disposed { get; private set; }

            /// <summary>Returns the next scripted latency.</summary>
            /// <param name="blockSeconds">The block duration, unused.</param>
            /// <returns>The scripted latency, or -1 when none is scripted.</returns>
            /// <exception cref="InvalidOperationException"><see cref="Throw"/> is set.</exception>
            public override double Measure(double blockSeconds)
            {
                if (Throw)
                {
                    throw new InvalidOperationException("probe failed");
                }
                if (Latencies.Count == 0)
                {
                    return -1;
                }
                var value = Latencies[Math.Min(_next, Latencies.Count - 1)];
                _next++;
                return value;
            }

            /// <summary>Counts the disposal.</summary>
            public override void Dispose()
            {
                Disposed++;
            }
        }
    }
}
