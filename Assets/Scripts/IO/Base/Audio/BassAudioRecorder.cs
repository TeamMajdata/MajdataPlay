#nullable enable
using AOT;
using MajdataPlay.Diagnostics;
using ManagedBass;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MajdataPlay.IO
{
    /// <summary>Captures the final mix of a Bass output channel through a DSP callback.</summary>
    /// <remarks>
    /// Each instance attaches its own DSP and owns its own buffer, so several recorders on one channel run independently.
    /// The DSP runs on the Bass output thread after every other DSP on the channel and copies the mix into a
    /// bounded buffer under a short lock. At most the front stereo pair of a multichannel device mix is kept,
    /// because <see cref="AudioManager.MixingMatrix"/> routes the stereo game mix to every speaker pair.
    /// <para>
    /// While recording, the DSP places every block on the playback timeline: a block is heard at its processing time plus
    /// the output latency reported by a <see cref="BassOutputLatencyProbe"/>. The first block sets <see cref="PlaybackDelay"/>.
    /// When the output thread stops delivering audio, for example while a blocking garbage collection suspends it, a later
    /// block is heard later than the frames already captured imply; silence for that gap is inserted before the block so
    /// that captured frame n is still heard at <see cref="PlaybackDelay"/> plus n divided by <see cref="SampleRate"/>.
    /// </para>
    /// </remarks>
    internal sealed class BassAudioRecorder : IAudioRecorder
    {
        /// <summary>Limits recorded channels to the front stereo pair.</summary>
        private const int MaximumRecordedChannels = 2;
        /// <summary>Places this DSP after all other DSP and FX on the channel so it observes the final mix.</summary>
        private const int DspPriority = int.MinValue;
        /// <summary>Sets how many block durations a block may be heard late before the time is treated as an output gap.</summary>
        /// <remarks>
        /// With a measured latency, a late callback that did not starve the device is offset by a smaller buffered amount, so its
        /// heard time stays put; only a real gap or clock drift moves it. Without per-block latency (ASIO's fixed driver latency or
        /// a missing measurement) scheduling jitter can still move a block by up to one block, so two blocks leave a full block
        /// of margin while any stall long enough to empty the device buffer is corrected.
        /// </remarks>
        private const int GapThresholdBlocks = 2;
        /// <summary>Sets the smallest lateness, in seconds, treated as an output gap when blocks are very short.</summary>
        private const double MinimumGapThresholdSeconds = 0.015;
        /// <summary>Sets how close, in seconds, the measured latency must return to its value before a gap for the output to count as refilled.</summary>
        /// <remarks>
        /// Right after an underrun the device buffer is empty, so the first blocks report a short latency, and they are actually
        /// played one or two device periods later than it implies (about 20 ms with BassSimple and WASAPI shared). Once the buffer
        /// has refilled, the latency returns to its steady value within a fraction of a millisecond and the remaining lateness is exact.
        /// </remarks>
        private const double SettleLatencyToleranceSeconds = 0.002;
        /// <summary>Sets how long after a gap, in seconds, the output may take to refill before the remaining lateness is filled anyway.</summary>
        private const double SettleTimeoutSeconds = 1;
        /// <summary>Sets the remaining lateness, in seconds, that is filled once a gap has settled; normal deviation stays below 1 ms.</summary>
        private const double SettleThresholdSeconds = 0.001;
        /// <summary>Roots the native DSP callback for IL2CPP and Mono.</summary>
        private static readonly DSPProcedure s_dspProcedure = OnDsp;
        /// <summary>Synchronizes the Bass output thread with Start, Stop, Read, and Dispose.</summary>
        private readonly object _gate = new object();
        /// <summary>Stores captured frames until the consumer reads them.</summary>
        private readonly AudioSampleRingBuffer _buffer;
        /// <summary>Stores the Bass channel whose output is captured.</summary>
        private readonly int _channel;
        /// <summary>Stores the interleaved channel count of the Bass channel.</summary>
        private readonly int _sourceChannels;
        /// <summary>Indicates whether the DSP receives 32-bit float samples rather than signed 16-bit samples.</summary>
        private readonly bool _isFloatSource;
        /// <summary>Measures how long captured blocks take to reach the output device.</summary>
        private readonly BassOutputLatencyProbe _latencyProbe;
        /// <summary>Stores the <see cref="Stopwatch"/> timestamp of the latest <see cref="Start"/>.</summary>
        private long _startTimestamp;
        /// <summary>Indicates whether a block has been captured since the latest <see cref="Start"/>.</summary>
        private bool _hasFirstBlock;
        /// <summary>Stores the time from <see cref="Start"/> until the first block was processed, in seconds.</summary>
        private double _firstBlockDelaySeconds;
        /// <summary>Stores the time from <see cref="Start"/> until the first captured frame was heard, in seconds.</summary>
        private double _firstFrameHeardSeconds;
        /// <summary>Counts the frames, including inserted silence, stored since the first block.</summary>
        private long _timelineFrames;
        /// <summary>Stores the latest valid output latency in seconds, or a negative value before any; kept across recordings.</summary>
        private double _lastOutputLatencySeconds = -1;
        /// <summary>Sums the output latencies measured since the latest <see cref="Start"/>, in seconds.</summary>
        private double _outputLatencySum;
        /// <summary>Counts the output latencies measured since the latest <see cref="Start"/>.</summary>
        private int _outputLatencyCount;
        /// <summary>Counts the output gaps filled with silence since the latest <see cref="Start"/>.</summary>
        private int _outputGapCount;
        /// <summary>Counts the silent frames inserted for output gaps since the latest <see cref="Start"/>.</summary>
        private long _insertedSilenceFrames;
        /// <summary>Stores the largest difference, in seconds, between a block's heard time and its timeline position that was left uncorrected.</summary>
        private double _largestTimelineDeviationSeconds;
        /// <summary>Stores the output latency, in seconds, used for the previous block.</summary>
        private double _previousBlockLatencySeconds;
        /// <summary>Indicates whether the output is refilling after the latest gap.</summary>
        private bool _isSettling;
        /// <summary>Stores the output latency, in seconds, of the last block before the latest gap.</summary>
        private double _settleReferenceLatencySeconds;
        /// <summary>Stores the processing time, in seconds after <see cref="Start"/>, after which the latest gap counts as settled.</summary>
        private double _settleDeadlineSeconds;
        /// <summary>Identifies this instance to the static DSP callback.</summary>
        private GCHandle _selfHandle;
        /// <summary>Stores the DSP handle, or zero after removal.</summary>
        private int _dsp;
        /// <summary>Indicates whether the DSP copies samples into the buffer.</summary>
        private bool _isRecording;
        /// <summary>Indicates whether the DSP has been removed and the recorder released.</summary>
        private bool _isDisposed;
        /// <summary>Stores the first failure inside the DSP callback, which cannot throw across the native boundary.</summary>
        private Exception? _captureError;

        /// <summary>Attaches a capture DSP to a Bass output channel.</summary>
        /// <param name="channel">The Bass mixer or device output stream whose final mix is captured.</param>
        /// <param name="bufferSeconds">The number of seconds of audio buffered for a slow consumer, at least one.</param>
        /// <param name="latencyProbe">The probe for the backend playing <paramref name="channel"/>; the recorder owns it once constructed.</param>
        /// <exception cref="ArgumentNullException"><paramref name="latencyProbe"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="bufferSeconds"/> is less than one.</exception>
        /// <exception cref="NotSupportedException">The channel uses 8-bit samples.</exception>
        /// <exception cref="InvalidOperationException">Bass cannot describe the channel or attach the DSP.</exception>
        public BassAudioRecorder(int channel, int bufferSeconds, BassOutputLatencyProbe latencyProbe)
        {
            if (bufferSeconds < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(bufferSeconds));
            }
            _latencyProbe = latencyProbe ?? throw new ArgumentNullException(nameof(latencyProbe));
            if (!Bass.ChannelGetInfo(channel, out var info))
            {
                throw new InvalidOperationException($"Cannot read the Bass output channel format: {Bass.LastError}");
            }
            // FloatingPointDSP makes Bass pass float samples to every DSP regardless of the channel format.
            _isFloatSource = info.Resolution == Resolution.Float || Bass.FloatingPointDSP;
            if (!_isFloatSource && info.Resolution != Resolution.Short)
            {
                throw new NotSupportedException("Recording an 8-bit Bass output channel is not supported.");
            }

            _channel = channel;
            _sourceChannels = info.Channels;
            SampleRate = info.Frequency;
            Channels = Math.Min(info.Channels, MaximumRecordedChannels);
            _buffer = new AudioSampleRingBuffer(Channels, checked(SampleRate * bufferSeconds));
            _selfHandle = GCHandle.Alloc(this);
            _dsp = Bass.ChannelSetDSP(channel, s_dspProcedure, GCHandle.ToIntPtr(_selfHandle), DspPriority);
            if (_dsp == 0)
            {
                var error = Bass.LastError;
                _selfHandle.Free();
                throw new InvalidOperationException($"Cannot attach the recording DSP to the Bass output channel: {error}");
            }
            MajDebug.LogInfo(nameof(BassAudioRecorder), $"Attached to channel {channel}: {SampleRate} Hz, "
                + $"{_sourceChannels} source channels, recording {Channels}, float={_isFloatSource}");
        }

        /// <inheritdoc/>
        public int SampleRate { get; }
        /// <inheritdoc/>
        public int Channels { get; }
        /// <inheritdoc/>
        public bool IsRecording
        {
            get
            {
                lock (_gate)
                {
                    return _isRecording;
                }
            }
        }
        /// <inheritdoc/>
        public long DroppedFrames
        {
            get
            {
                lock (_gate)
                {
                    return _buffer.DroppedFrames;
                }
            }
        }

        /// <inheritdoc/>
        public TimeSpan PlaybackDelay
        {
            get
            {
                lock (_gate)
                {
                    return _hasFirstBlock ? ToTimeSpan(_firstFrameHeardSeconds) : TimeSpan.Zero;
                }
            }
        }
        /// <summary>Gets how many output gaps have been filled with silence since the latest <see cref="Start"/>.</summary>
        internal int OutputGapCount
        {
            get
            {
                lock (_gate)
                {
                    return _outputGapCount;
                }
            }
        }
        /// <summary>Gets how much silence has been inserted for output gaps since the latest <see cref="Start"/>.</summary>
        internal TimeSpan InsertedSilence
        {
            get
            {
                lock (_gate)
                {
                    return ToTimeSpan(_insertedSilenceFrames / (double)SampleRate);
                }
            }
        }
        /// <summary>Gets the largest difference between a block's heard time and its timeline position that was left uncorrected.</summary>
        internal TimeSpan LargestTimelineDeviation
        {
            get
            {
                lock (_gate)
                {
                    return ToTimeSpan(_largestTimelineDeviationSeconds);
                }
            }
        }

        /// <inheritdoc/>
        public void Start()
        {
            lock (_gate)
            {
                EnsureIsOpen();
                if (_isRecording)
                {
                    return;
                }
                _buffer.Clear();
                _captureError = null;
                _startTimestamp = Stopwatch.GetTimestamp();
                _hasFirstBlock = false;
                _firstBlockDelaySeconds = 0;
                _firstFrameHeardSeconds = 0;
                _timelineFrames = 0;
                _outputLatencySum = 0;
                _outputLatencyCount = 0;
                _outputGapCount = 0;
                _insertedSilenceFrames = 0;
                _largestTimelineDeviationSeconds = 0;
                _previousBlockLatencySeconds = 0;
                _isSettling = false;
                _settleReferenceLatencySeconds = 0;
                _settleDeadlineSeconds = 0;
                _isRecording = true;
            }
        }

        /// <inheritdoc/>
        public void Stop()
        {
            Exception? captureError;
            long droppedFrames;
            double playbackDelaySeconds;
            double firstBlockDelaySeconds;
            double averageLatencySeconds;
            int latencyMeasurements;
            int outputGaps;
            double insertedSilenceSeconds;
            double largestDeviationSeconds;
            lock (_gate)
            {
                EnsureIsOpen();
                if (!_isRecording)
                {
                    return;
                }
                _isRecording = false;
                captureError = _captureError;
                droppedFrames = _buffer.DroppedFrames;
                playbackDelaySeconds = _hasFirstBlock ? _firstFrameHeardSeconds : 0;
                firstBlockDelaySeconds = _firstBlockDelaySeconds;
                averageLatencySeconds = _outputLatencyCount == 0 ? 0 : _outputLatencySum / _outputLatencyCount;
                latencyMeasurements = _outputLatencyCount;
                outputGaps = _outputGapCount;
                insertedSilenceSeconds = _insertedSilenceFrames / (double)SampleRate;
                largestDeviationSeconds = _largestTimelineDeviationSeconds;
            }
            if (captureError is not null)
            {
                MajDebug.LogError(nameof(BassAudioRecorder), $"Audio capture failed during recording: {captureError}");
            }
            if (droppedFrames > 0)
            {
                MajDebug.LogWarning(nameof(BassAudioRecorder), $"Dropped {droppedFrames} frames because the consumer fell behind");
            }
            if (outputGaps > 0)
            {
                MajDebug.LogWarning(nameof(BassAudioRecorder), $"The output stopped delivering audio {outputGaps} times; inserted "
                    + $"{insertedSilenceSeconds * 1000:F1} ms of silence to keep the recording on the playback timeline");
            }
            MajDebug.LogInfo(nameof(BassAudioRecorder), $"The first captured frame was played {playbackDelaySeconds * 1000:F1} ms after Start "
                + $"(first block {firstBlockDelaySeconds * 1000:F1} ms, average output latency {averageLatencySeconds * 1000:F1} ms "
                + $"from {latencyMeasurements} measurements, largest uncorrected timeline deviation {largestDeviationSeconds * 1000:F1} ms)");
        }

        /// <inheritdoc/>
        public int Read(Span<float> destination)
        {
            lock (_gate)
            {
                EnsureIsOpen();
                return _buffer.Read(destination);
            }
        }

        /// <summary>Removes the capture DSP and discards buffered audio.</summary>
        /// <remarks>Repeated disposal has no effect. The Bass output channel itself is not freed.</remarks>
        public void Dispose()
        {
            lock (_gate)
            {
                if (_isDisposed)
                {
                    return;
                }
                _isDisposed = true;
                _isRecording = false;
            }
            // Bass serializes DSP removal with the channel's DSP chain, so no callback runs after this returns.
            if (_dsp != 0 && !Bass.ChannelRemoveDSP(_channel, _dsp))
            {
                MajDebug.LogWarning(nameof(BassAudioRecorder), $"Cannot remove the recording DSP: {Bass.LastError}");
            }
            _dsp = 0;
            _latencyProbe.Dispose();
            if (_selfHandle.IsAllocated)
            {
                _selfHandle.Free();
            }
        }

        /// <summary>Converts seconds to a <see cref="TimeSpan"/> without the millisecond rounding of <see cref="TimeSpan.FromSeconds(double)"/> on older runtimes.</summary>
        /// <param name="seconds">The duration in seconds.</param>
        /// <returns>The duration with tick precision.</returns>
        private static TimeSpan ToTimeSpan(double seconds)
        {
            return TimeSpan.FromTicks((long)(seconds * TimeSpan.TicksPerSecond));
        }

        /// <summary>Places one block of the final mix on the playback timeline and copies it into the buffer while recording.</summary>
        /// <param name="buffer">The borrowed native sample block.</param>
        /// <param name="length">The block length in bytes.</param>
        private unsafe void Capture(IntPtr buffer, int length)
        {
            lock (_gate)
            {
                if (!_isRecording)
                {
                    return;
                }
                var processedSeconds = (Stopwatch.GetTimestamp() - _startTimestamp) / (double)Stopwatch.Frequency;
                var sampleBytes = _isFloatSource ? sizeof(float) : sizeof(short);
                var blockSeconds = length / sampleBytes / _sourceChannels / (double)SampleRate;
                var measuredLatency = _latencyProbe.Measure(blockSeconds);
                if (measuredLatency >= 0)
                {
                    _lastOutputLatencySeconds = measuredLatency;
                    _outputLatencySum += measuredLatency;
                    _outputLatencyCount++;
                }
                // Sounds started before this block is mixed play from its first frame, so the block's first frame is heard after its latency.
                var outputLatency = Math.Max(_lastOutputLatencySeconds, 0);
                var heardSeconds = processedSeconds + outputLatency;
                if (!_hasFirstBlock)
                {
                    _hasFirstBlock = true;
                    _firstBlockDelaySeconds = processedSeconds;
                    _firstFrameHeardSeconds = heardSeconds;
                }
                else
                {
                    FillOutputGap(processedSeconds, outputLatency, blockSeconds);
                }
                _previousBlockLatencySeconds = outputLatency;
                int storedFrames;
                if (_isFloatSource)
                {
                    storedFrames = _buffer.Write(new ReadOnlySpan<float>((void*)buffer, length / sizeof(float)), _sourceChannels);
                }
                else
                {
                    storedFrames = _buffer.Write(new ReadOnlySpan<short>((void*)buffer, length / sizeof(short)), _sourceChannels);
                }
                _timelineFrames += storedFrames;
            }
        }

        /// <summary>Inserts silence when a block is heard later than the frames already stored imply; call while holding the gate.</summary>
        /// <param name="processedSeconds">When the block passed the capture DSP, in seconds after <see cref="Start"/>.</param>
        /// <param name="outputLatency">The output latency of the block in seconds.</param>
        /// <param name="blockSeconds">The duration of the block.</param>
        /// <remarks>
        /// A block late by more than the gap threshold gets the missing time as silence at once. The output then refills its buffer;
        /// once its latency is back to the value before the gap, the steady lateness left by the short latencies reported during the
        /// refill is filled as well. Frames lost because the buffer was full leave the timeline short too, so they are repaired the
        /// same way once space is free. Blocks heard earlier than expected are never shifted, so slow clock drift below the gap
        /// threshold stays uncorrected.
        /// </remarks>
        private void FillOutputGap(double processedSeconds, double outputLatency, double blockSeconds)
        {
            var expectedSeconds = _firstFrameHeardSeconds + (_timelineFrames / (double)SampleRate);
            var latenessSeconds = processedSeconds + outputLatency - expectedSeconds;
            var thresholdSeconds = Math.Max(GapThresholdBlocks * blockSeconds, MinimumGapThresholdSeconds);
            if (latenessSeconds > thresholdSeconds)
            {
                InsertSilence(latenessSeconds);
                _outputGapCount++;
                if (!_isSettling)
                {
                    _settleReferenceLatencySeconds = _previousBlockLatencySeconds;
                }
                _isSettling = true;
                _settleDeadlineSeconds = processedSeconds + SettleTimeoutSeconds;
                return;
            }
            if (_isSettling)
            {
                var isRefilled = Math.Abs(outputLatency - _settleReferenceLatencySeconds) <= SettleLatencyToleranceSeconds;
                if (!isRefilled && processedSeconds < _settleDeadlineSeconds)
                {
                    return;
                }
                _isSettling = false;
                if (latenessSeconds > SettleThresholdSeconds)
                {
                    InsertSilence(latenessSeconds);
                    return;
                }
            }
            _largestTimelineDeviationSeconds = Math.Max(_largestTimelineDeviationSeconds, Math.Abs(latenessSeconds));
        }

        /// <summary>Appends silence to the buffer and the timeline; call while holding the gate.</summary>
        /// <param name="seconds">The duration of the silence.</param>
        private void InsertSilence(double seconds)
        {
            var silentFrames = (int)Math.Min(seconds * SampleRate, int.MaxValue);
            var storedFrames = _buffer.WriteSilence(silentFrames);
            _timelineFrames += storedFrames;
            _insertedSilenceFrames += storedFrames;
        }

        /// <summary>Records a callback failure for the next Stop without throwing on the Bass thread.</summary>
        /// <param name="error">The failure raised while capturing.</param>
        private void ReportCaptureError(Exception error)
        {
            lock (_gate)
            {
                _captureError ??= error;
            }
        }

        /// <summary>Throws when the recorder has been disposed; call while holding the gate.</summary>
        /// <exception cref="ObjectDisposedException">The recorder has been disposed.</exception>
        private void EnsureIsOpen()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(BassAudioRecorder));
            }
        }

        /// <summary>Forwards a Bass DSP block to the recorder identified by the user pointer.</summary>
        /// <param name="handle">The DSP handle.</param>
        /// <param name="channel">The channel the DSP is attached to.</param>
        /// <param name="buffer">The borrowed sample block.</param>
        /// <param name="length">The block length in bytes.</param>
        /// <param name="user">The GCHandle pointer of the recorder.</param>
        [MonoPInvokeCallback(typeof(DSPProcedure))]
        private static void OnDsp(int handle, int channel, IntPtr buffer, int length, IntPtr user)
        {
            BassAudioRecorder? recorder = null;
            try
            {
                recorder = GCHandle.FromIntPtr(user).Target as BassAudioRecorder;
                recorder?.Capture(buffer, length);
            }
            catch (Exception error)
            {
                recorder?.ReportCaptureError(error);
            }
        }
    }
}
