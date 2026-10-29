using UnityEngine;

#nullable enable
namespace MajdataPlay.Diagnostics
{
    /// <summary>Holds the averaged frame timing values of one completed measurement window, in milliseconds.</summary>
    internal readonly struct FrameLatencySnapshot
    {
        /// <summary>Gets the number of frames in the window.</summary>
        public int SampleCount { get; }

        /// <summary>Gets the average main thread work time, excluding the present wait.</summary>
        public double MainThreadMs { get; }

        /// <summary>Gets the average time the main thread waited for the render thread or the swapchain.</summary>
        public double PresentWaitMs { get; }

        /// <summary>Gets the average render thread work time.</summary>
        public double RenderThreadMs { get; }

        /// <summary>Gets the average GPU time of one frame.</summary>
        public double GpuMs { get; }

        /// <summary>Gets the average time from the start of a frame on the main thread to the render thread calling Present.</summary>
        public double StartToPresentMs { get; }

        /// <summary>Gets the average time from the start of a frame on the main thread to the GPU completing it, or a negative value when unavailable.</summary>
        public double StartToGpuCompleteMs { get; }

        /// <summary>Gets the largest time from the start of a frame on the main thread to the GPU completing it, or a negative value when unavailable.</summary>
        public double MaxStartToGpuCompleteMs { get; }

        /// <summary>Creates a snapshot.</summary>
        /// <param name="sampleCount">The number of frames in the window.</param>
        /// <param name="mainThreadMs">The average main thread work time.</param>
        /// <param name="presentWaitMs">The average main thread present wait time.</param>
        /// <param name="renderThreadMs">The average render thread work time.</param>
        /// <param name="gpuMs">The average GPU frame time.</param>
        /// <param name="startToPresentMs">The average frame start to Present call time.</param>
        /// <param name="startToGpuCompleteMs">The average frame start to GPU completion time, or a negative value when unavailable.</param>
        /// <param name="maxStartToGpuCompleteMs">The largest frame start to GPU completion time, or a negative value when unavailable.</param>
        public FrameLatencySnapshot(int sampleCount, double mainThreadMs, double presentWaitMs, double renderThreadMs,
            double gpuMs, double startToPresentMs, double startToGpuCompleteMs, double maxStartToGpuCompleteMs)
        {
            SampleCount = sampleCount;
            MainThreadMs = mainThreadMs;
            PresentWaitMs = presentWaitMs;
            RenderThreadMs = renderThreadMs;
            GpuMs = gpuMs;
            StartToPresentMs = startToPresentMs;
            StartToGpuCompleteMs = startToGpuCompleteMs;
            MaxStartToGpuCompleteMs = maxStartToGpuCompleteMs;
        }
    }

    /// <summary>
    /// Accumulates <see cref="FrameTiming"/> samples and averages them, including the CPU timestamp differences that
    /// show how many frames are queued between the start of a frame and its completion on the GPU.
    /// </summary>
    internal sealed class FrameLatencyStatistics
    {
        /// <summary>The number of accumulated frames.</summary>
        private int _sampleCount;

        /// <summary>The number of accumulated frames that carried a GPU completion timestamp.</summary>
        private int _gpuCompleteSampleCount;

        /// <summary>The sum of the main thread work times.</summary>
        private double _mainThreadSumMs;

        /// <summary>The sum of the main thread present wait times.</summary>
        private double _presentWaitSumMs;

        /// <summary>The sum of the render thread work times.</summary>
        private double _renderThreadSumMs;

        /// <summary>The sum of the GPU frame times.</summary>
        private double _gpuSumMs;

        /// <summary>The sum of the frame start to Present call times.</summary>
        private double _startToPresentSumMs;

        /// <summary>The sum of the frame start to GPU completion times.</summary>
        private double _startToGpuCompleteSumMs;

        /// <summary>The largest frame start to GPU completion time.</summary>
        private double _maxStartToGpuCompleteMs;

        /// <summary>Gets the number of accumulated frames.</summary>
        public int SampleCount => _sampleCount;

        /// <summary>Adds one frame.</summary>
        /// <param name="timing">The frame timing reported by <see cref="FrameTimingManager"/>.</param>
        /// <param name="cpuTimerFrequency">The frequency of the CPU timestamps in ticks per second.</param>
        /// <returns>Whether the frame was accepted; frames without a valid Present timestamp are ignored.</returns>
        public bool AddSample(in FrameTiming timing, ulong cpuTimerFrequency)
        {
            if (cpuTimerFrequency == 0 || timing.cpuTimePresentCalled < timing.frameStartTimestamp
                || timing.frameStartTimestamp == 0)
            {
                return false;
            }

            var millisecondsPerTick = 1000.0 / cpuTimerFrequency;
            _sampleCount++;
            _mainThreadSumMs += timing.cpuMainThreadFrameTime;
            _presentWaitSumMs += timing.cpuMainThreadPresentWaitTime;
            _renderThreadSumMs += timing.cpuRenderThreadFrameTime;
            _gpuSumMs += timing.gpuFrameTime;
            _startToPresentSumMs += (timing.cpuTimePresentCalled - timing.frameStartTimestamp) * millisecondsPerTick;

            // Some platforms do not report GPU completion; keep those frames out of the GPU completion average.
            if (timing.cpuTimeFrameComplete >= timing.cpuTimePresentCalled)
            {
                var startToGpuCompleteMs = (timing.cpuTimeFrameComplete - timing.frameStartTimestamp) * millisecondsPerTick;
                _gpuCompleteSampleCount++;
                _startToGpuCompleteSumMs += startToGpuCompleteMs;
                if (startToGpuCompleteMs > _maxStartToGpuCompleteMs)
                {
                    _maxStartToGpuCompleteMs = startToGpuCompleteMs;
                }
            }

            return true;
        }

        /// <summary>Averages the accumulated frames.</summary>
        /// <returns>The averages, with zero samples and zero values when nothing was accumulated.</returns>
        public FrameLatencySnapshot GetSnapshot()
        {
            if (_sampleCount == 0)
            {
                return new FrameLatencySnapshot(0, 0, 0, 0, 0, 0, -1, -1);
            }

            var hasGpuComplete = _gpuCompleteSampleCount > 0;
            return new FrameLatencySnapshot(_sampleCount,
                _mainThreadSumMs / _sampleCount,
                _presentWaitSumMs / _sampleCount,
                _renderThreadSumMs / _sampleCount,
                _gpuSumMs / _sampleCount,
                _startToPresentSumMs / _sampleCount,
                hasGpuComplete ? _startToGpuCompleteSumMs / _gpuCompleteSampleCount : -1,
                hasGpuComplete ? _maxStartToGpuCompleteMs : -1);
        }

        /// <summary>Discards all accumulated frames.</summary>
        public void Reset()
        {
            _sampleCount = 0;
            _gpuCompleteSampleCount = 0;
            _mainThreadSumMs = 0;
            _presentWaitSumMs = 0;
            _renderThreadSumMs = 0;
            _gpuSumMs = 0;
            _startToPresentSumMs = 0;
            _startToGpuCompleteSumMs = 0;
            _maxStartToGpuCompleteMs = 0;
        }
    }
}
