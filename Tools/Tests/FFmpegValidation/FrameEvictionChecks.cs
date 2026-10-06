#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;
using MajdataPlay.FFmpeg;
using MajdataPlay.FFmpeg.Internal;
using MajdataPlay.FFmpeg.Interop;

namespace MajdataPlay.FFmpeg.Validation
{
    /// <summary>Verifies worker-owned frame eviction using actual FFmpeg decoding and native frame resources.</summary>
    internal static class FrameEvictionChecks
    {
        /// <summary>Counts assertions completed by the real decoder checks.</summary>
        private static int s_checks;

        /// <summary>Checks bounded preloading, autonomous eviction, pause, seek revisions, and frame release.</summary>
        /// <param name="media">The seekable fixture with at least two seconds of video at 24 FPS or higher.</param>
        /// <returns>The number of assertions completed during this invocation.</returns>
        /// <exception cref="InvalidOperationException">The fixture or an observed playback invariant is invalid.</exception>
        /// <exception cref="TimeoutException">A worker operation does not finish within its finite timeout.</exception>
        public static int RunNative(string media)
        {
            var before = s_checks;
            foreach (var capacity in new[] { 1, 3, 8 })
            {
                TestCapacity(media, capacity);
            }

            TestEndOfStream(media);
            TestExpiredFrameDiscard(media);
            TestKeyFrameCatchUp(media);
            return s_checks - before;
        }

        /// <summary>Checks the real worker synchronization used by Windows Vulkan's D3D11VA fallback.</summary>
        /// <param name="media">A seekable D3D11VA-compatible fixture with nonuniform pixels after its first second.</param>
        /// <returns>The number of assertions completed during this invocation.</returns>
        /// <exception cref="InvalidOperationException">Synchronization, frame pixels, or native ownership is invalid.</exception>
        /// <exception cref="NotSupportedException">The GPU or bridge lacks D3D11VA synchronization support.</exception>
        public static int RunDecodeSynchronization(string media)
        {
            var before = s_checks;
            for (var iteration = 0; iteration < 3; iteration++)
            {
                TestDecodeSynchronization(media);
            }

            TestSynchronizedWorkerClose(media);
            return s_checks - before;
        }

        /// <summary>Decodes real native frames, reuses the query across seeks, and audits device references after disposal.</summary>
        /// <param name="media">The real native fixture decoded without Unity graphics initialization.</param>
        /// <exception cref="InvalidOperationException">A decoded frame, codec configuration, or native reference is invalid.</exception>
        private static unsafe void TestDecodeSynchronization(string media)
        {
            using var audit = new DecodeSynchronizationAudit();
            using var cancellation = new CancellationTokenSource();
            using var decoder = new FFmpegVideoDecoder(audit.CreateOptions());
            decoder.Open(media, cancellation.Token);
            Check(decoder.HardwareDecoding, "Admission synchronization preserves actual D3D11VA hardware decoding.");
            Check(audit.Synchronization != null, "The native transport installs production D3D11VA admission synchronization.");
            var codec = (AVCodecContext*)Pointer.Unbox(ReadField<object>(decoder, "_codec"));
            Check(codec->thread_count == 1,
                "Synchronized hardware decoding disables asynchronous codec workers that could bypass completion markers.");
            Check(decoder.CanSeek && decoder.Duration > 2, "The synchronization fixture supports forward and backward seeking.");
            var previous = double.NegativeInfinity;
            for (var index = 0; index < 24; index++)
            {
                using var frame = decoder.ReadFrame();
                Check(frame != null && frame.IsHardwareFrame && frame.HardwareDecoded
                    && frame.NativeFrame != IntPtr.Zero && frame.Data == IntPtr.Zero,
                    "Completed hardware frames retain native D3D11VA resources without CPU transport.");
                Check(frame!.PixelFormat == AVPixelFormat.AV_PIX_FMT_D3D11 && frame.PresentationTime >= previous,
                    "Synchronized native frames have D3D11VA format and monotonic presentation timestamps.");
                previous = frame.PresentationTime;
            }

            Check(audit.Synchronization!.WaitCount >= 24 && audit.Synchronization.WaitCount <= 32,
                "Hardware outputs complete without redundant markers on every empty receive and packet send.");
            // No playback deadline is installed here: every output frame is decoded,
            // unlike a 60 FPS presenter that intentionally supersedes older due frames.
            var decodeWatch = Stopwatch.StartNew();
            var decodedFrames = 0;
            for (var index = 0; index < 240; index++)
            {
                using var frame = decoder.ReadFrame();
                if (frame == null)
                {
                    break;
                }

                Check(frame.IsHardwareFrame && frame.PresentationTime >= previous,
                    "Unthrottled decode retains native hardware output and monotonic timestamps.");
                previous = frame.PresentationTime;
                decodedFrames++;
            }

            decodeWatch.Stop();
            Check(decodedFrames > 0, "The fixture contains hardware outputs after query warmup.");
            Console.WriteLine("D3D11VA unthrottled decode: " + decodedFrames + " frames / "
                + decodeWatch.Elapsed.TotalSeconds.ToString("F3") + " s = "
                + (decodedFrames / decodeWatch.Elapsed.TotalSeconds).ToString("F1") + " FPS; source=" + decoder.FrameRate + " FPS.");
            var target = Math.Min(1.25, decoder.Duration / 2);
            decoder.Seek(target);
            using (var frame = decoder.ReadFrame())
            {
                Check(frame != null && frame.PresentationTime + frame.Duration >= target - 0.05
                    && frame.PresentationTime < target + 0.2,
                    "GPU admission remains reusable while forward seek decodes preroll frames.");
                AssertHardwarePixels(frame!);
            }

            decoder.Seek(0);
            using var held = decoder.ReadFrame();
            Check(held != null && held.IsHardwareFrame && held.PresentationTime < 0.2,
                "Backward seek resets the codec while preserving reusable worker synchronization.");
            cancellation.Cancel();
            var canceled = false;
            var watch = Stopwatch.StartNew();
            try
            {
                using var ignored = decoder.ReadFrame();
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            Check(canceled && watch.Elapsed.TotalSeconds < 1,
                "Canceling synchronized decoding is observed before another codec submission.");
            decoder.Dispose();
            decoder.Dispose();
            Check(audit.Synchronization.DisposeCount == 1,
                "Closing the decoder disposes its production completion query exactly once.");
            using (var copied = held!.CopyToSoftware())
            {
                Check(copied.Data != IntPtr.Zero && copied.HardwareDecoded,
                    "A presenter-owned native frame remains downloadable after the decoder and query close.");
            }

            held.Dispose();
            Check(audit.DeviceReferenceCount == 1,
                "Only the test audit retains the hardware device after decoder, query, and native frames are released.");
            Console.WriteLine("D3D11VA decode synchronization: " + audit.Synchronization.WaitCount
                + " completed GPU markers; native pixels, seeks, cancellation and ownership passed.");
        }

        /// <summary>Closes the real synchronized worker during high-rate playback and audits its final ownership state.</summary>
        /// <param name="media">The seekable fixture decoded on the production session worker.</param>
        /// <exception cref="InvalidOperationException">The worker faults, exceeds its queue, or retains a device after close.</exception>
        /// <exception cref="TimeoutException">The worker does not preload or close within the finite timeout.</exception>
        private static void TestSynchronizedWorkerClose(string media)
        {
            using var audit = new DecodeSynchronizationAudit();
            using var session = new VideoDecodeSession(media, audit.CreateOptions(), 3);
            WaitFor(session, () => session.BufferedFrames == 3, "synchronized hardware preload");
            using var held = session.TakeFrame();
            Check(held != null && held.IsHardwareFrame && held.HardwareDecoded,
                "The production worker preloads native D3D11VA frames with admission control.");
            session.SetPlayback(0, 16, true);
            Thread.Sleep(100);
            Check(session.Error == null && session.BufferedFrames <= 3,
                "A 16x playback clock keeps synchronized hardware frames within the fixed queue capacity.");
            var watch = Stopwatch.StartNew();
            session.Dispose();
            Check(watch.Elapsed.TotalSeconds < 0.5, "Closing high-rate hardware playback does not join its GPU worker.");
            WaitFor(session, () => WorkerFinished(session), "synchronized hardware cancellation and cleanup");
            Check(audit.Synchronization != null && audit.Synchronization.DisposeCount == 1,
                "Worker cancellation releases its production D3D11VA completion query.");
            held!.Dispose();
            Check(audit.DeviceReferenceCount == 1,
                "Closing a high-rate session releases all device references after the presenter returns its frame.");
        }

        /// <summary>Downloads actual native hardware pixels and checks that RGBA conversion preserves image variation.</summary>
        /// <param name="frame">The borrowed native frame whose owned source remains alive during download.</param>
        /// <exception cref="InvalidOperationException">The hardware download is empty, changes ownership, or contains no image variation.</exception>
        private static unsafe void AssertHardwarePixels(DecodedVideoFrame frame)
        {
            var native = frame.NativeFrame;
            using var copied = frame.CopyToSoftware();
            Check(copied.HardwareDecoded && !copied.IsHardwareFrame && copied.Data != IntPtr.Zero
                && copied.DataSize == copied.Width * copied.Height * 4,
                "A completed native frame downloads to independently owned packed RGBA pixels.");
            var pixels = (byte*)copied.Data;
            var minimum = 765;
            var maximum = 0;
            for (var offset = 0; offset < copied.DataSize; offset += 4)
            {
                var brightness = pixels[offset] + pixels[offset + 1] + pixels[offset + 2];
                minimum = Math.Min(minimum, brightness);
                maximum = Math.Max(maximum, brightness);
            }

            Check(maximum - minimum > 15 && frame.NativeFrame == native && frame.Data == IntPtr.Zero,
                "Real hardware pixels survive completion synchronization without consuming their native source.");
        }

        /// <summary>Retains one audit reference to the real device and records the production synchronizer's lifecycle.</summary>
        private sealed unsafe class DecodeSynchronizationAudit : IDisposable
        {
            /// <summary>Owns one independent device reference used only to check release after session cleanup.</summary>
            private IntPtr _device;
            /// <summary>Gets the wrapper around the real native synchronizer created on the decoding worker.</summary>
            internal TrackedDecodeSynchronization? Synchronization { get; private set; }
            /// <summary>Gets the remaining FFmpeg device references while the audit reference keeps the device alive.</summary>
            internal int DeviceReferenceCount
            {
                get
                {
                    return ffmpeg.av_buffer_get_ref_count((AVBufferRef*)_device);
                }
            }

            /// <summary>Creates strict native D3D11VA options using the real production admission callback.</summary>
            /// <returns>The options to pass to one decoder or one worker session.</returns>
            internal DecoderOptions CreateOptions()
            {
                return new DecoderOptions
                {
                    HardwareDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                    KeepNativeFrames = true,
                    RequireHardwareDecoding = true,
                    AllowHardwareCpuUpload = false,
                    ThreadCount = 16,
                    AcquireHardwareDevice = AcquireDevice,
                    CreateHardwareSynchronization = CreateSynchronization
                };
            }

            /// <summary>Creates a real independent D3D11VA device and retains a separate reference for cleanup assertions.</summary>
            /// <returns>An owned FFmpeg device reference transferred to the decoder.</returns>
            /// <exception cref="InvalidOperationException">A device was already created or FFmpeg cannot create D3D11VA.</exception>
            /// <exception cref="OutOfMemoryException">FFmpeg cannot allocate the audit reference.</exception>
            private IntPtr AcquireDevice()
            {
                if (_device != IntPtr.Zero)
                {
                    throw new InvalidOperationException("The synchronization audit expects one hardware device per session.");
                }

                AVBufferRef* device = null;
                FFmpegVideoDecoder.Check(ffmpeg.av_hwdevice_ctx_create(&device,
                    AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0), "Create synchronized hardware test device");
                var retained = ffmpeg.av_buffer_ref(device);
                if (retained == null)
                {
                    ffmpeg.av_buffer_unref(&device);
                    throw new OutOfMemoryException("Cannot retain the synchronization audit device.");
                }

                _device = (IntPtr)retained;
                return (IntPtr)device;
            }

            /// <summary>Wraps the production admission synchronizer without replacing any native completion behavior.</summary>
            /// <param name="device">The borrowed FFmpeg D3D11VA device supplied to the native bridge.</param>
            /// <returns>A worker-owned synchronizer with lifecycle counters for assertions.</returns>
            /// <exception cref="NotSupportedException">The bridge cannot create the real completion query.</exception>
            private IHardwareDecodeSynchronization CreateSynchronization(IntPtr device)
            {
                Synchronization = new TrackedDecodeSynchronization(VulkanVideoInterop.CreateD3D11Synchronization(device));
                return Synchronization;
            }

            /// <summary>Releases the audit's final FFmpeg device reference after all ownership assertions finish.</summary>
            public void Dispose()
            {
                var device = (AVBufferRef*)_device;
                _device = IntPtr.Zero;
                ffmpeg.av_buffer_unref(&device);
            }
        }

        /// <summary>Observes calls and disposal while forwarding every operation to the production synchronizer.</summary>
        private sealed class TrackedDecodeSynchronization : IHardwareDecodeSynchronization
        {
            /// <summary>Owns the production synchronizer and its retained native query/device resources.</summary>
            private readonly IHardwareDecodeSynchronization _inner;
            /// <summary>Counts completed worker-side GPU completion markers.</summary>
            private int _waitCount;
            /// <summary>Counts decoder-owned disposal calls on the owning worker.</summary>
            private int _disposeCount;
            /// <summary>Gets the number of waits that reached actual GPU completion.</summary>
            internal int WaitCount
            {
                get
                {
                    return Volatile.Read(ref _waitCount);
                }
            }
            /// <summary>Gets the number of disposal calls forwarded to the native synchronizer.</summary>
            internal int DisposeCount
            {
                get
                {
                    return Volatile.Read(ref _disposeCount);
                }
            }

            /// <summary>Transfers ownership of the production synchronizer into the observing wrapper.</summary>
            /// <param name="inner">The production synchronizer whose native behavior is exercised.</param>
            internal TrackedDecodeSynchronization(IHardwareDecodeSynchronization inner)
            {
                _inner = inner;
            }

            /// <summary>Waits using the real query and records successful completion.</summary>
            /// <param name="cancellationToken">Cancels the production GPU wait.</param>
            /// <param name="timeoutMilliseconds">Bounds the production GPU wait in milliseconds.</param>
            /// <exception cref="OperationCanceledException">The session was canceled.</exception>
            /// <exception cref="TimeoutException">GPU completion exceeded the configured timeout.</exception>
            /// <exception cref="NotSupportedException">The native completion query failed.</exception>
            public void Wait(CancellationToken cancellationToken, int timeoutMilliseconds)
            {
                _inner.Wait(cancellationToken, timeoutMilliseconds);
                Interlocked.Increment(ref _waitCount);
            }

            /// <summary>Releases the actual native query and records its decoder-owned disposal.</summary>
            public void Dispose()
            {
                _inner.Dispose();
                Interlocked.Increment(ref _disposeCount);
            }
        }

        /// <summary>Checks that expired frames are discarded before conversion while presentation follows a fast clock.</summary>
        /// <param name="media">The seekable native video fixture played at 1x and 16x.</param>
        /// <exception cref="InvalidOperationException">Frames are discarded at 1x, kept at 16x, or presentation falls behind.</exception>
        /// <exception cref="TimeoutException">The native worker does not preload within the finite timeout.</exception>
        private static void TestExpiredFrameDiscard(string media)
        {
            using var session = new VideoDecodeSession(media, new DecoderOptions(), 3);
            WaitFor(session, () => session.BufferedFrames == 3, "discard fixture preload");
            var info = session.Info!;
            session.SetPlayback(0, 1, true);
            PresentFor(session, 500, 8, out _, out _);
            Check(session.DiscardedFrames == 0, "Real-time playback with a prompt presenter discards no decoded frames.");

            // A presenter at roughly 60 Hz consumes far fewer frames than 16x playback makes due.
            session.SetPlayback(rate: 16);
            PresentFor(session, Math.Min(1500, (int)(info.Duration / 16 * 1000 * 0.6)), 16, out var presented, out var maximumLag);
            Check(session.DiscardedFrames > presented,
                "At 16x, expired frames are discarded on the worker instead of converted for presentation.");
            Check(maximumLag <= 0.25 * 16 + 2,
                "Presented frames stay within the catch-up window of the 16x playback clock (lag " + maximumLag.ToString("F2") + " s).");
            Check(session.Error == null, "Discarding expired frames does not fault the worker.");
            Console.WriteLine("Expired frame discard: " + session.DiscardedFrames + " discarded, " + presented
                + " presented, " + session.CatchUpSeeks + " catch-up seeks, max lag " + maximumLag.ToString("F2") + " s.");
        }

        /// <summary>Checks that a decoder far behind a running clock skips to a later keyframe instead of decoding the gap.</summary>
        /// <param name="media">The seekable native video fixture, long enough to contain a keyframe after its midpoint.</param>
        /// <exception cref="InvalidOperationException">No catch-up seek occurs or the queue does not reach the clock.</exception>
        /// <exception cref="TimeoutException">The worker does not catch up within the finite timeout.</exception>
        private static void TestKeyFrameCatchUp(string media)
        {
            using var session = new VideoDecodeSession(media, new DecoderOptions(), 3);
            WaitFor(session, () => session.BufferedFrames == 3, "catch-up fixture preload");
            var info = session.Info!;
            var target = info.Duration * 0.5;
            // Moving a running clock without Seek models a decoder that fell far behind.
            session.SetPlayback(target, 1, true);
            WaitFor(session, () => session.NextPresentationTime >= target - (2 / info.FrameRate), "keyframe catch-up");
            Check(session.CatchUpSeeks >= 1, "A decoder far behind the running clock repositions to a later keyframe.");
            Check(session.Error == null, "Keyframe catch-up does not fault the worker.");
            using (var frame = session.TakeLatestFrame(double.PositiveInfinity))
            {
                Check(frame != null && frame.Data != IntPtr.Zero && frame.PresentationTime >= target - (2 / info.FrameRate),
                    "Frames after keyframe catch-up carry pixels at or after the playback clock.");
            }
        }

        /// <summary>Consumes due frames at a fixed interval as the player would, measuring how far behind the clock they are.</summary>
        /// <param name="session">The playing session whose due frames are taken.</param>
        /// <param name="milliseconds">The total wall-clock duration of the simulated presenter.</param>
        /// <param name="intervalMilliseconds">The wall-clock interval between simulated presentation attempts.</param>
        /// <param name="presented">Receives the number of frames taken for presentation.</param>
        /// <param name="maximumLag">Receives the largest clock position minus presented frame end, in media seconds.</param>
        /// <exception cref="InvalidOperationException">The worker fails or presented timestamps move backward.</exception>
        private static void PresentFor(VideoDecodeSession session, int milliseconds, int intervalMilliseconds, out int presented, out double maximumLag)
        {
            presented = 0;
            maximumLag = 0;
            var previous = double.NegativeInfinity;
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds)
            {
                if (session.Error != null)
                {
                    throw new InvalidOperationException("Frame discard worker failed.", session.Error);
                }

                var position = session.PlaybackPosition;
                using (var frame = session.TakeLatestFrame(position + 0.001))
                {
                    if (frame != null)
                    {
                        Check(frame.PresentationTime >= previous, "Presented timestamps never move backward while catching up.");
                        previous = frame.PresentationTime;
                        maximumLag = Math.Max(maximumLag, position - (frame.PresentationTime + frame.Duration));
                        presented++;
                    }
                }

                Thread.Sleep(intervalMilliseconds);
            }
        }

        /// <summary>Checks that autonomous EOF draining preserves the final frame and permits a later backward seek.</summary>
        /// <param name="media">The seekable native video fixture used for final-frame decoding.</param>
        /// <exception cref="InvalidOperationException">The final frame or EOF ownership semantics are invalid.</exception>
        /// <exception cref="TimeoutException">The native worker does not drain or seek within the finite timeout.</exception>
        private static void TestEndOfStream(string media)
        {
            using var session = new VideoDecodeSession(media, new DecoderOptions(), 3);
            WaitFor(session, () => session.BufferedFrames == 3, "EOF fixture preload");
            var info = session.Info!;
            var tolerance = Math.Max(0.1, 2 / info.FrameRate);
            session.Seek(info.Duration - 0.5);
            WaitFor(session, () => session.BufferedFrames == 3, "near-end preload");
            session.SetPlayback(info.Duration + 1, 2, true);
            WaitFor(session, () => DecoderDrained(session), "autonomous native EOF drain");
            Check(session.BufferedFrames == 1 && !session.EndOfStream,
                "Autonomous EOF draining preserves one final due frame until the presenter takes it.");
            using (var final = session.TakeLatestFrame(double.PositiveInfinity))
            {
                Check(final != null && final.PresentationTime >= info.Duration - tolerance
                    && final.PresentationTime <= info.Duration + tolerance && final.Data != IntPtr.Zero,
                    "The retained final frame carries the last native video timestamp and pixels.");
                Check(session.EndOfStream, "EOF becomes observable only after the final queued frame is transferred.");
            }

            session.Seek(0);
            Check(session.PlaybackPosition == 0, "A post-EOF seek resets the shared clock immediately.");
            WaitFor(session, () => session.BufferedFrames == 3, "backward seek after EOF");
            Check(!session.EndOfStream && session.NextPresentationTime <= tolerance,
                "A backward seek clears EOF and does not reuse the past-end eviction cutoff.");
            AssertStable(session, Snapshot(session), 100, "A post-EOF seek preserves its first preloaded frames.");
            Check(session.PlaybackPosition == 0, "A post-EOF seek leaves the shared clock paused.");
        }

        /// <summary>Exercises one real decode session without consuming frames while the clock advances.</summary>
        /// <param name="media">The seekable fixture decoded by the worker.</param>
        /// <param name="capacity">The bounded presentation queue capacity to exercise.</param>
        /// <exception cref="InvalidOperationException">A fixture requirement or session invariant fails.</exception>
        /// <exception cref="TimeoutException">The worker does not complete a requested operation.</exception>
        private static void TestCapacity(string media, int capacity)
        {
            using var session = new VideoDecodeSession(media, new DecoderOptions(), capacity, 2);
            Check(ClockRate(session) == 2 && session.PlaybackPosition == 0,
                "A pre-open rate of two is configured before the session clock starts.");
            WaitFor(session, () => session.BufferedFrames == capacity, "initial preload");
            var info = session.Info!;
            Check(info.CanSeek && info.Duration > 2 && info.FrameRate >= 24,
                "Eviction fixture must be seekable, longer than two seconds, and at least 24 FPS.");
            var interval = 1 / info.FrameRate;
            using var held = session.TakeFrame() ?? throw new InvalidOperationException("Presenter did not obtain its initial frame.");
            var pixels = held.Data;
            var sample = Marshal.ReadByte(pixels);
            WaitFor(session, () => session.BufferedFrames == capacity, "refill with a presenter-held frame");
            var initial = Snapshot(session);
            Check(initial.Length == capacity, "Preloading respects queue capacity " + capacity + ".");
            AssertStable(session, initial, 100, "Preloading does not advance the queue.");
            session.SetPlayback(position: 0.5, playing: false);
            AssertStable(session, initial, 100, "A paused timeline does not evict preloaded frames.");
            Check(session.PlaybackPosition == 0.5, "A paused clock retains its explicitly configured position.");

            // No TakeFrame calls or subsequent clock publications wake this worker.
            session.SetPlayback(playing: true);
            Check(ClockRate(session) == 2, "Starting playback preserves the rate supplied before opening.");
            WaitFor(session, () => session.NextPresentationTime >= 0.5 - (interval * 2),
                "catch up to an already advancing playback clock");
            var caughtUp = session.NextPresentationTime;
            Check(caughtUp > initial[0] + 0.25, "Worker evicts obsolete frames without presenter consumption.");
            WaitFor(session, () => session.NextPresentationTime >= caughtUp + 0.12,
                "autonomous timed wake at double speed");
            Check(session.BufferedFrames <= capacity, "Autonomous eviction keeps its queue bounded.");
            var rateChangeHead = session.NextPresentationTime;
            var beforeRateChange = session.PlaybackPosition;
            var rateChangeWatch = Stopwatch.StartNew();
            session.SetPlayback(rate: 16);
            var afterRateChange = session.PlaybackPosition;
            Check(afterRateChange >= beforeRateChange
                && afterRateChange - beforeRateChange <= (rateChangeWatch.Elapsed.TotalSeconds * 16) + 0.01,
                "A rate-only update preserves the shared clock position continuously.");
            WaitFor(session, () => session.NextPresentationTime >= rateChangeHead + (interval * 2),
                "rate change wakes the full queue worker");
            Check(session.BufferedFrames <= capacity, "A faster rate preserves the fixed queue capacity.");

            session.SetPlayback(playing: false);
            var pausedPosition = session.PlaybackPosition;
            WaitFor(session, () => session.BufferedFrames == capacity, "paused queue refill");
            AssertStable(session, Snapshot(session), 150, "Pausing stops worker-owned timeline advancement.");
            Check(session.PlaybackPosition == pausedPosition, "A pause-only update freezes the shared playback clock.");

            session.SetPlayback(1.5, 2, true);
            WaitFor(session, () => session.NextPresentationTime >= 1.5 - (interval * 2), "forward catch-up");
            session.Seek(0.25);
            Check(session.PlaybackPosition == 0.25, "Seek updates the shared playback position immediately.");
            WaitFor(session, () => session.BufferedFrames == capacity, "backward seek preload");
            var backwards = Snapshot(session);
            Check(Math.Abs(backwards[0] - 0.25) <= Math.Max(0.1, interval * 2),
                "Backward seek presents its first target frame instead of applying the old eviction cutoff.");
            AssertStable(session, backwards, 100, "Seek leaves eviction disabled until playback is published again.");
            Check(session.PlaybackPosition == 0.25, "Seek leaves the shared clock paused while frames preload.");
            session.SetPlayback(playing: true);
            WaitFor(session, () => session.PlaybackPosition > 0.25 + interval,
                "playing-only resume after seek");
            Check(ClockRate(session) == 2, "A playing-only update resumes the existing rate after seek.");

            session.Seek(0.75);
            session.Seek(0.3);
            session.Seek(0.65);
            session.Seek(0.4);
            WaitFor(session, () => session.BufferedFrames == capacity, "superseding seeks");
            var latest = Snapshot(session);
            Check(Math.Abs(latest[0] - 0.4) <= Math.Max(0.1, interval * 2),
                "Only the latest seek revision supplies queued frames.");

            session.SetPlayback(0, 0.0625, true);
            AssertStable(session, latest, 100, "A full queue of future frames is retained.");
            using (var early = session.TakeLatestFrame(0))
            {
                Check(early == null && session.BufferedFrames == capacity,
                    "Due-filtered presentation does not consume a future frame after worker eviction.");
            }

            // A slow clock allows the real worker to hold its future candidate
            // while the existing full queue remains unchanged.
            session.SetPlayback(latest[0], 0.0625, true);
            WaitFor(session, () => AvailableFrames(session) == 0, "pending future candidate with a presenter-held frame");
            session.SetPlayback(0, 0.0625, true);
            var future = Snapshot(session);
            var futureWindow = Math.Max(1, Math.Min(100, (int)(interval * 0.25 / 0.0625 * 1000)));
            AssertStable(session, future, futureWindow, "A future decoded candidate does not replace future queue entries.");
            session.Dispose();
            var closedPosition = session.PlaybackPosition;
            WaitFor(session, () => WorkerFinished(session), "close with a pending candidate");
            Thread.Sleep(20);
            Check(session.PlaybackPosition == closedPosition, "Closing freezes the shared session clock.");
            Check(session.BufferedFrames == 0 && AvailableFrames(session) == capacity + 1,
                "Closing returns queued and pending frames while preserving the presenter's lease.");
            Check(held.Data == pixels && Marshal.ReadByte(pixels) == sample,
                "Worker eviction, repeated seeks, and close preserve the presenter's owned pixels.");
            held.Dispose();
            Check(AvailableFrames(session) == capacity + 2,
                "Returning the presenter's frame restores the complete fixed pool.");
            Check(session.Error == null, "Eviction, seek revisions, and close do not exhaust the frame pool.");
            Console.WriteLine("Frame eviction capacity " + capacity + ": real decode, autonomous clock, pause, seek and release passed.");
        }

        /// <summary>Reads queued timestamps under the production session lock without transferring ownership.</summary>
        /// <param name="session">The real session whose queue is observed.</param>
        /// <returns>The ordered timestamps currently held by the bounded presentation queue.</returns>
        /// <exception cref="MissingFieldException">The session no longer exposes an expected private implementation field.</exception>
        private static double[] Snapshot(VideoDecodeSession session)
        {
            var gate = ReadField<object>(session, "_gate");
            lock (gate)
            {
                var queue = ReadField<Queue<DecodedVideoFrame>>(session, "_frames");
                var result = new double[queue.Count];
                var index = 0;
                foreach (var frame in queue)
                {
                    result[index++] = frame.PresentationTime;
                }

                return result;
            }
        }

        /// <summary>Counts returned frame containers while holding the frame pool lock.</summary>
        /// <param name="session">The session that owns the fixed frame pool.</param>
        /// <returns>The number of frame containers currently available for rental.</returns>
        /// <exception cref="MissingFieldException">A required private pool field is missing.</exception>
        private static int AvailableFrames(VideoDecodeSession session)
        {
            var pool = ReadField<DecodedVideoFramePool>(session, "_framePool");
            lock (ReadField<object>(pool, "_gate"))
            {
                return ReadField<int>(pool, "_count");
            }
        }

        /// <summary>Reads the configured multiplier of the session's single clock under its ownership lock.</summary>
        /// <param name="session">The real session whose configured clock rate is checked.</param>
        /// <returns>The multiplier currently used by both worker eviction and presentation.</returns>
        /// <exception cref="MissingFieldException">The expected private clock or lock field is missing.</exception>
        private static double ClockRate(VideoDecodeSession session)
        {
            lock (ReadField<object>(session, "_gate"))
            {
                return ReadField<PlaybackClock>(session, "_playbackClock").Rate;
            }
        }

        /// <summary>Observes worker completion under the session lock without joining the presentation thread.</summary>
        /// <param name="session">The closed session whose worker completion is observed.</param>
        /// <returns>Whether the worker has finished releasing its native resources.</returns>
        /// <exception cref="MissingFieldException">The expected completion field is missing.</exception>
        private static bool WorkerFinished(VideoDecodeSession session)
        {
            lock (ReadField<object>(session, "_gate"))
            {
                return ReadField<bool>(session, "_finished");
            }
        }

        /// <summary>Reads the actual native decoder's drained status under the session lock.</summary>
        /// <param name="session">The real session whose EOF state is inspected.</param>
        /// <returns>Whether native decoding has returned EOF even if a final frame remains queued.</returns>
        /// <exception cref="MissingFieldException">The expected private EOF field is missing.</exception>
        private static bool DecoderDrained(VideoDecodeSession session)
        {
            lock (ReadField<object>(session, "_gate"))
            {
                return ReadField<bool>(session, "_eof");
            }
        }

        /// <summary>Reads one private field for resource and concurrency assertions in the real session.</summary>
        /// <typeparam name="T">The expected field value type.</typeparam>
        /// <param name="owner">The instance containing the private field.</param>
        /// <param name="name">The exact private field name to inspect.</param>
        /// <returns>The field value cast to its declared test type.</returns>
        /// <exception cref="MissingFieldException">The requested private field is absent.</exception>
        /// <exception cref="InvalidCastException">The field has an unexpected value type.</exception>
        private static T ReadField<T>(object owner, string name)
        {
            var field = owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                throw new MissingFieldException(owner.GetType().FullName, name);
            }

            return (T)field.GetValue(owner)!;
        }

        /// <summary>Checks that every queued timestamp remains unchanged during a bounded observation.</summary>
        /// <param name="session">The real session being observed.</param>
        /// <param name="expected">The timestamps expected throughout the observation.</param>
        /// <param name="milliseconds">The observation duration in wall-clock milliseconds.</param>
        /// <param name="message">The invariant described when an assertion fails.</param>
        /// <exception cref="InvalidOperationException">The queue changes unexpectedly.</exception>
        private static void AssertStable(VideoDecodeSession session, double[] expected, int milliseconds, string message)
        {
            var watch = Stopwatch.StartNew();
            do
            {
                var actual = Snapshot(session);
                if (actual.Length != expected.Length)
                {
                    Check(false, message);
                }
                for (var index = 0; index < actual.Length; index++)
                {
                    if (actual[index] != expected[index])
                    {
                        Check(false, message);
                    }
                }

                Thread.Sleep(5);
            }
            while (watch.ElapsedMilliseconds < milliseconds);
            Check(true, message);
        }

        /// <summary>Waits for a worker state while reporting native failures and bounding the wait.</summary>
        /// <param name="session">The real session whose worker errors are checked.</param>
        /// <param name="ready">The condition that ends the wait.</param>
        /// <param name="operation">The operation named in timeout diagnostics.</param>
        /// <exception cref="TimeoutException">The worker does not satisfy the condition within twenty seconds.</exception>
        private static void WaitFor(VideoDecodeSession session, Func<bool> ready, string operation)
        {
            var watch = Stopwatch.StartNew();
            while (!ready())
            {
                if (session.Error != null)
                {
                    throw new InvalidOperationException("Frame eviction worker failed during " + operation + ".", session.Error);
                }

                if (watch.Elapsed.TotalSeconds > 20)
                {
                    throw new TimeoutException("Frame eviction worker did not complete " + operation + ".");
                }

                Thread.Sleep(2);
            }
        }

        /// <summary>Records an assertion and reports a failed real-session invariant.</summary>
        /// <param name="condition">Whether the observed invariant holds.</param>
        /// <param name="message">The expected invariant included in failure diagnostics.</param>
        /// <exception cref="InvalidOperationException">The asserted condition is false.</exception>
        private static void Check(bool condition, string message)
        {
            s_checks++;
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
