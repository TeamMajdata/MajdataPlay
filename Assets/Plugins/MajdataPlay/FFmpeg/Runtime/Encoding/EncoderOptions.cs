#nullable enable
using System;
using UnityEngine;

namespace MajdataPlay.FFmpeg
{
    /// <summary>Identifies the implementation performing video compression.</summary>
    public enum VideoEncoderType
    {
        /// <summary>Compresses frames with a CPU encoder.</summary>
        Software,
        /// <summary>Compresses frames with a hardware encoder, with software fallback when preferred.</summary>
        Hardware
    }

    /// <summary>Identifies the requested compressed video format without allowing implicit format substitution.</summary>
    public enum VideoEncodingFormat
    {
        /// <summary>Uses H.264/AVC compression.</summary>
        H264,
        /// <summary>Uses H.265/HEVC compression.</summary>
        HEVC,
        /// <summary>Uses VP9 compression.</summary>
        VP9,
        /// <summary>Uses AV1 compression.</summary>
        AV1,
        /// <summary>Uses FFmpeg's built-in MPEG-4 Part 2 software encoder.</summary>
        MPEG4
    }

    /// <summary>Identifies the configured encoder rate control algorithm.</summary>
    public enum VideoRateControlMode
    {
        /// <summary>Requests constant bitrate control; packet sizes can still vary.</summary>
        CBR,
        /// <summary>Requests variable bitrate control with a target and an encoder rate limit.</summary>
        VBR
    }

    /// <summary>Configures video recording dimensions, codec selection, threading, and rate control.</summary>
    /// <remarks>Recording takes a validated copy; changes apply only to the next recording.</remarks>
    [Serializable]
    public sealed class EncoderOptions
    {
        /// <summary>Gets or sets the even output width in pixels, between 2 and 16384.</summary>
        [field: SerializeField]
        public int Width { get; set; } = 1920;
        /// <summary>Gets or sets the even output height in pixels, between 2 and 16384.</summary>
        [field: SerializeField]
        public int Height { get; set; } = 1080;
        /// <summary>Gets or sets the integral output frame rate between 1 and 240.</summary>
        [field: SerializeField]
        public int FrameRate { get; set; } = 60;
        /// <summary>Gets or sets the preferred backend; hardware preference permits software fallback within the requested format.</summary>
        [field: SerializeField]
        public VideoEncoderType PreferredEncoderType { get; set; } = VideoEncoderType.Software;
        /// <summary>Gets or sets the maximum FFmpeg software codec worker count, between 1 and 128.</summary>
        /// <remarks>This controls codec workers, not all auxiliary threads created by an external library or driver.</remarks>
        [field: SerializeField]
        public int MaximumSoftwareThreads { get; set; } = Math.Min(Environment.ProcessorCount, 8);
        /// <summary>Gets or sets the requested compressed video format.</summary>
        [field: SerializeField]
        public VideoEncodingFormat Format { get; set; } = VideoEncodingFormat.H264;
        /// <summary>Gets or sets the target video bitrate in bits per second, at least 1000.</summary>
        /// <remarks>In CBR mode this is the constant-rate target and the effective maximum rate.</remarks>
        [field: SerializeField]
        public long BitRate { get; set; } = 8000000;
        /// <summary>Gets or sets the upper encoder rate in bits per second, at least the target bitrate and no greater than Int32.MaxValue.</summary>
        /// <remarks>VBV encoders apply a buffered limit; libvpx/libaom use budgets that can overshoot in either mode. Individual packets are not bounded.</remarks>
        [field: SerializeField]
        public long MaximumBitRate { get; set; } = 16000000;
        /// <summary>Gets or sets the requested rate control mode; unsupported combinations fail explicitly.</summary>
        [field: SerializeField]
        public VideoRateControlMode RateControlMode { get; set; } = VideoRateControlMode.VBR;

        /// <summary>Validates settings and creates an independent snapshot for one recording.</summary>
        /// <returns>A copy of the validated settings.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Dimensions, frame rate, thread count, bitrate, or an enum value are invalid.</exception>
        internal EncoderOptions ValidateAndClone()
        {
            if (Width < 2 || Width > 16384 || (Width & 1) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(Width), "Width must be even and between 2 and 16384.");
            }

            if (Height < 2 || Height > 16384 || (Height & 1) != 0 || (long)Width * Height * 4 > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(Height), "Height must be even, between 2 and 16384, and fit an RGBA allocation.");
            }

            if (FrameRate < 1 || FrameRate > 240)
            {
                throw new ArgumentOutOfRangeException(nameof(FrameRate), "Frame rate must be between 1 and 240.");
            }

            if (MaximumSoftwareThreads < 1 || MaximumSoftwareThreads > 128)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumSoftwareThreads), "Software worker count must be between 1 and 128.");
            }

            if (BitRate < 1000 || MaximumBitRate < BitRate || MaximumBitRate > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumBitRate), "Bitrates must satisfy 1000 <= target <= maximum <= Int32.MaxValue.");
            }

            if (!Enum.IsDefined(typeof(VideoEncoderType), PreferredEncoderType)
                || !Enum.IsDefined(typeof(VideoEncodingFormat), Format)
                || !Enum.IsDefined(typeof(VideoRateControlMode), RateControlMode))
            {
                throw new ArgumentOutOfRangeException(nameof(Format), "An encoder option has an unknown enum value.");
            }

            return new EncoderOptions
            {
                Width = Width,
                Height = Height,
                FrameRate = FrameRate,
                PreferredEncoderType = PreferredEncoderType,
                MaximumSoftwareThreads = MaximumSoftwareThreads,
                Format = Format,
                BitRate = BitRate,
                MaximumBitRate = MaximumBitRate,
                RateControlMode = RateControlMode
            };
        }
    }
}
