#nullable enable
using AOT;
using ManagedBass;
#if UNITY_STANDALONE_WIN
using ManagedBass.Asio;
using ManagedBass.Wasapi;
#endif
using System;

namespace MajdataPlay.IO
{
    /// <summary>Measures how long audio passing a Bass output channel's DSP chain takes to be played by the device.</summary>
    /// <remarks>
    /// <see cref="BassAudioRecorder"/> calls <see cref="Measure"/> from its capture DSP on the Bass output thread, where the backend's
    /// buffer state belongs to the block being captured. Each backend reports its output buffering differently, so each has its own
    /// probe. Dispose a probe only after the DSP that uses it has been removed.
    /// </remarks>
    internal abstract class BassOutputLatencyProbe : IDisposable
    {
        /// <summary>Measures the delay until the first frame of the block passing the capture DSP is played.</summary>
        /// <param name="blockSeconds">The duration of the block passing the capture DSP.</param>
        /// <returns>The delay in seconds, or a negative value while the backend cannot report it.</returns>
        public abstract double Measure(double blockSeconds);

        /// <summary>Releases the resources owned by the probe.</summary>
        public virtual void Dispose()
        {
        }

        /// <summary>Creates a probe for the BassSimple device output stream.</summary>
        /// <param name="deviceStream">The device output stream whose final mix is captured.</param>
        /// <returns>A probe that owns a silent unbuffered playback stream on the same device.</returns>
        /// <exception cref="InvalidOperationException">Bass cannot describe the device stream or create and start the silent stream.</exception>
        public static BassOutputLatencyProbe CreateForDeviceStream(int deviceStream)
        {
            return new DeviceStreamProbe(deviceStream);
        }

#if UNITY_STANDALONE_WIN
        /// <summary>Creates a probe for the global mixer pulled by the initialized WASAPI output.</summary>
        /// <returns>A probe that reads the data buffered by BASSWASAPI.</returns>
        /// <exception cref="InvalidOperationException">BASSWASAPI cannot describe the output.</exception>
        public static BassOutputLatencyProbe CreateForWasapi()
        {
            return new WasapiProbe();
        }

        /// <summary>Creates a probe for the global mixer pulled by the started ASIO output.</summary>
        /// <returns>A probe that reports the latency of the ASIO driver.</returns>
        public static BassOutputLatencyProbe CreateForAsio()
        {
            return new AsioProbe();
        }
#endif

        /// <summary>Compares the rendered and played positions of a silent channel mixed in the same device update as the capture.</summary>
        private sealed class DeviceStreamProbe : BassOutputLatencyProbe
        {
            /// <summary>Roots the native silence callback for IL2CPP and Mono.</summary>
            private static readonly StreamProcedure s_silence = WriteSilence;
            /// <summary>Stores the silent unbuffered playback stream.</summary>
            private readonly int _stream;

            /// <summary>Creates and starts the silent stream at the device stream's sample rate.</summary>
            /// <param name="deviceStream">The device output stream whose final mix is captured.</param>
            /// <exception cref="InvalidOperationException">Bass cannot describe the device stream or create and start the silent stream.</exception>
            public DeviceStreamProbe(int deviceStream)
            {
                if (!Bass.ChannelGetInfo(deviceStream, out var info))
                {
                    throw new InvalidOperationException($"Cannot read the Bass device stream format: {Bass.LastError}");
                }
                // Zero bytes are silence in every sample format, so the format only needs to be one the device accepts.
                _stream = Bass.CreateStream(info.Frequency, 2, BassFlags.Float, s_silence);
                if (_stream == 0)
                {
                    _stream = Bass.CreateStream(info.Frequency, 2, BassFlags.Default, s_silence);
                }
                if (_stream == 0)
                {
                    throw new InvalidOperationException($"Cannot create the output latency probe stream: {Bass.LastError}");
                }
                // Without playback buffering the stream is rendered in the device update, together with the captured block.
                if (!Bass.ChannelSetAttribute(_stream, ChannelAttribute.Buffer, 0) || !Bass.ChannelPlay(_stream))
                {
                    var error = Bass.LastError;
                    Bass.StreamFree(_stream);
                    throw new InvalidOperationException($"Cannot start the output latency probe stream: {error}");
                }
            }

            /// <inheritdoc/>
            public override double Measure(double blockSeconds)
            {
                var rendered = Bass.ChannelGetPosition(_stream, PositionFlags.Bytes | PositionFlags.Decode);
                var played = Bass.ChannelGetPosition(_stream, PositionFlags.Bytes);
                if (rendered < 0 || played < 0)
                {
                    return -1;
                }
                // The rendered position already includes the block passing the capture DSP.
                return Bass.ChannelBytes2Seconds(_stream, rendered - played) - blockSeconds;
            }

            /// <inheritdoc/>
            public override void Dispose()
            {
                Bass.StreamFree(_stream);
            }

            /// <summary>Fills a block of the probe stream with silence.</summary>
            /// <param name="handle">The probe stream.</param>
            /// <param name="buffer">The buffer to fill.</param>
            /// <param name="length">The buffer length in bytes.</param>
            /// <param name="user">The unused user pointer.</param>
            /// <returns>The number of bytes written, always the whole buffer.</returns>
            [MonoPInvokeCallback(typeof(StreamProcedure))]
            private static unsafe int WriteSilence(int handle, IntPtr buffer, int length, IntPtr user)
            {
                new Span<byte>((void*)buffer, length).Clear();
                return length;
            }
        }

#if UNITY_STANDALONE_WIN
        /// <summary>Reads how much audio BASSWASAPI has buffered ahead of the device, including its asynchronous buffer.</summary>
        private sealed class WasapiProbe : BassOutputLatencyProbe
        {
            /// <summary>Stores the byte rate of the 32-bit float data that BASSWASAPI buffers.</summary>
            private readonly double _bytesPerSecond;

            /// <summary>Reads the format of the initialized output.</summary>
            /// <exception cref="InvalidOperationException">BASSWASAPI cannot describe the output.</exception>
            public WasapiProbe()
            {
                if (!BassWasapi.GetInfo(out var info))
                {
                    throw new InvalidOperationException($"Cannot read the WASAPI output format: {Bass.LastError}");
                }
                // BASSWASAPI exchanges 32-bit float data with the application whatever the device format is.
                _bytesPerSecond = (double)info.Frequency * info.Channels * sizeof(float);
            }

            /// <inheritdoc/>
            public override double Measure(double blockSeconds)
            {
                // The capture DSP runs while BASSWASAPI renders the mixer for the device, before its block is added to the buffered data.
                var buffered = BassWasapi.GetData(IntPtr.Zero, (int)DataFlags.Available);
                return buffered < 0 ? -1 : buffered / _bytesPerSecond;
            }
        }

        /// <summary>Reports the output latency of the ASIO driver, which is fixed while the device runs.</summary>
        private sealed class AsioProbe : BassOutputLatencyProbe
        {
            /// <summary>Stores the driver's output latency in seconds, or a negative value when it is unknown.</summary>
            private readonly double _latencySeconds;

            /// <summary>Reads the driver's output latency, which is only valid after the device has started.</summary>
            public AsioProbe()
            {
                var samples = BassAsio.GetLatency(false);
                var rate = BassAsio.Rate;
                _latencySeconds = samples > 0 && rate > 0 ? samples / rate : -1;
            }

            /// <inheritdoc/>
            public override double Measure(double blockSeconds)
            {
                return _latencySeconds;
            }
        }
#endif
    }
}
