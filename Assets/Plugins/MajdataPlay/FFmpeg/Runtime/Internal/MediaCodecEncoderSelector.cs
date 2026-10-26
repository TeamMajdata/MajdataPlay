#nullable enable
using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataPlay.FFmpeg.Internal
{
    /// <summary>Describes one Android MediaCodec encoder component's capabilities for a single MIME type.</summary>
    /// <remarks>Values come from <c>MediaCodecList</c>; the selector never infers hardware support from a component name.</remarks>
    internal sealed class MediaCodecEncoderCandidate
    {
        /// <summary>Gets the exact MediaCodec component name passed to FFmpeg's <c>codec_name</c> option.</summary>
        public string Name { get; }
        /// <summary>Gets whether Android reports the component as an alias of another component.</summary>
        public bool IsAlias { get; }
        /// <summary>Gets whether Android reports the component as hardware accelerated.</summary>
        public bool IsHardwareAccelerated { get; }
        /// <summary>Gets whether Android reports the component as software only.</summary>
        public bool IsSoftwareOnly { get; }
        /// <summary>Gets the Android color-format constants accepted as encoder input.</summary>
        public IReadOnlyList<int> ColorFormats { get; }
        /// <summary>Gets whether the component supports the configured size and frame rate.</summary>
        public bool SupportsSizeAndRate { get; }
        /// <summary>Gets whether the requested target bitrate lies inside the component's bitrate range.</summary>
        public bool SupportsBitRate { get; }
        /// <summary>Gets whether the component declares support for the requested bitrate mode.</summary>
        public bool SupportsBitRateMode { get; }

        /// <summary>Creates an immutable candidate description.</summary>
        /// <param name="name">The exact MediaCodec component name.</param>
        /// <param name="isAlias">Whether the component is an alias.</param>
        /// <param name="isHardwareAccelerated">Whether Android reports hardware acceleration.</param>
        /// <param name="isSoftwareOnly">Whether Android reports a software-only implementation.</param>
        /// <param name="colorFormats">The Android input color-format constants; copied by this constructor.</param>
        /// <param name="supportsSizeAndRate">Whether the configured size and frame rate are supported.</param>
        /// <param name="supportsBitRate">Whether the target bitrate is inside the supported range.</param>
        /// <param name="supportsBitRateMode">Whether the requested bitrate mode is supported.</param>
        /// <exception cref="ArgumentException">The name is null or empty.</exception>
        /// <exception cref="ArgumentNullException">The color formats are null.</exception>
        public MediaCodecEncoderCandidate(string name, bool isAlias, bool isHardwareAccelerated, bool isSoftwareOnly,
            IReadOnlyList<int> colorFormats, bool supportsSizeAndRate, bool supportsBitRate, bool supportsBitRateMode)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A MediaCodec component name is required.", nameof(name));
            }

            if (colorFormats == null)
            {
                throw new ArgumentNullException(nameof(colorFormats));
            }

            var formats = new int[colorFormats.Count];
            for (var i = 0; i < formats.Length; i++)
            {
                formats[i] = colorFormats[i];
            }

            Name = name;
            IsAlias = isAlias;
            IsHardwareAccelerated = isHardwareAccelerated;
            IsSoftwareOnly = isSoftwareOnly;
            ColorFormats = formats;
            SupportsSizeAndRate = supportsSizeAndRate;
            SupportsBitRate = supportsBitRate;
            SupportsBitRateMode = supportsBitRateMode;
        }
    }

    /// <summary>Identifies the hardware MediaCodec component and CPU input format chosen for one recording.</summary>
    internal readonly struct MediaCodecEncoderSelection
    {
        /// <summary>Gets the exact MediaCodec component name passed to FFmpeg's <c>codec_name</c> option.</summary>
        public string ComponentName { get; }
        /// <summary>Gets the CPU pixel format that FFmpeg maps to the component's declared input color format.</summary>
        public AVPixelFormat PixelFormat { get; }

        /// <summary>Creates a selection result.</summary>
        /// <param name="componentName">The exact MediaCodec component name.</param>
        /// <param name="pixelFormat">The FFmpeg CPU pixel format to submit.</param>
        public MediaCodecEncoderSelection(string componentName, AVPixelFormat pixelFormat)
        {
            ComponentName = componentName;
            PixelFormat = pixelFormat;
        }
    }

    /// <summary>Maps recording formats to FFmpeg MediaCodec wrappers and selects a verified hardware component.</summary>
    /// <remarks>Selection is pure so that it can be tested without an Android device; the Android query only supplies candidates.</remarks>
    internal static class MediaCodecEncoderSelector
    {
        /// <summary>Android's <c>COLOR_FormatYUV420Planar</c>, mapped by FFmpeg to <see cref="AVPixelFormat.AV_PIX_FMT_YUV420P"/>.</summary>
        internal const int ColorFormatYuv420Planar = 19;
        /// <summary>Android's <c>COLOR_FormatYUV420SemiPlanar</c>, mapped by FFmpeg to <see cref="AVPixelFormat.AV_PIX_FMT_NV12"/>.</summary>
        internal const int ColorFormatYuv420SemiPlanar = 21;
        /// <summary>The first Android API level whose <c>MediaCodecInfo</c> reports hardware acceleration explicitly.</summary>
        internal const int MinimumHardwareClassificationApiLevel = 29;

        /// <summary>Gets FFmpeg's MediaCodec encoder wrapper name for a recording format.</summary>
        /// <param name="format">The requested compressed format.</param>
        /// <returns>The wrapper name, or null when no MediaCodec wrapper is used for the format.</returns>
        public static string? GetEncoderName(VideoEncodingFormat format)
        {
            return format switch
            {
                VideoEncodingFormat.H264 => "h264_mediacodec",
                VideoEncodingFormat.HEVC => "hevc_mediacodec",
                VideoEncodingFormat.VP9 => "vp9_mediacodec",
                VideoEncodingFormat.AV1 => "av1_mediacodec",
                _ => null
            };
        }

        /// <summary>Gets the MediaCodec MIME type that FFmpeg's wrapper configures for a recording format.</summary>
        /// <param name="format">The requested compressed format.</param>
        /// <returns>The MIME type, or null when no MediaCodec wrapper is used for the format.</returns>
        public static string? GetMimeType(VideoEncodingFormat format)
        {
            return format switch
            {
                VideoEncodingFormat.H264 => "video/avc",
                VideoEncodingFormat.HEVC => "video/hevc",
                VideoEncodingFormat.VP9 => "video/x-vnd.on2.vp9",
                VideoEncodingFormat.AV1 => "video/av01",
                _ => null
            };
        }

        /// <summary>Gets the coded size that FFmpeg's wrapper configures for CPU input.</summary>
        /// <param name="format">The requested compressed format.</param>
        /// <param name="width">The output width in pixels.</param>
        /// <param name="height">The output height in pixels.</param>
        /// <returns>The coded width and height; H.264 and HEVC are padded to 16 and cropped in the bitstream.</returns>
        public static (int Width, int Height) GetConfiguredSize(VideoEncodingFormat format, int width, int height)
        {
            if (format == VideoEncodingFormat.H264 || format == VideoEncodingFormat.HEVC)
            {
                return ((width + 15) & ~15, (height + 15) & ~15);
            }

            return (width, height);
        }

        /// <summary>Selects the first hardware-only component that supports the requested input, size, rate, and bitrate mode.</summary>
        /// <param name="candidates">The encoder components declaring the requested MIME type, in <c>MediaCodecList</c> order.</param>
        /// <param name="selection">Receives the selected component and input format when this method returns true.</param>
        /// <param name="failures">Receives one diagnostic for each rejected component, or a single diagnostic when none exist.</param>
        /// <returns>True when a component was selected.</returns>
        /// <exception cref="ArgumentNullException">The candidates or failures are null.</exception>
        public static bool TrySelect(IReadOnlyList<MediaCodecEncoderCandidate> candidates, out MediaCodecEncoderSelection selection,
            StringBuilder failures)
        {
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            if (failures == null)
            {
                throw new ArgumentNullException(nameof(failures));
            }

            selection = default;
            if (candidates.Count == 0)
            {
                failures.Append("MediaCodec reports no encoder component for this format; ");
                return false;
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var rejection = GetRejection(candidate, out var pixelFormat);
                if (rejection == null)
                {
                    selection = new MediaCodecEncoderSelection(candidate.Name, pixelFormat);
                    return true;
                }

                failures.Append("MediaCodec ").Append(candidate.Name).Append(": ").Append(rejection).Append("; ");
            }

            return false;
        }

        /// <summary>Checks one candidate against the hardware-only and configuration requirements.</summary>
        /// <param name="candidate">The candidate to check.</param>
        /// <param name="pixelFormat">Receives the preferred CPU input format when the candidate is accepted.</param>
        /// <returns>Null when accepted, otherwise the rejection reason.</returns>
        private static string? GetRejection(MediaCodecEncoderCandidate candidate, out AVPixelFormat pixelFormat)
        {
            pixelFormat = AVPixelFormat.AV_PIX_FMT_NONE;
            if (candidate.IsAlias)
            {
                return "alias component";
            }

            if (!candidate.IsHardwareAccelerated || candidate.IsSoftwareOnly)
            {
                return "not a hardware-only component";
            }

            pixelFormat = ChoosePixelFormat(candidate.ColorFormats);
            if (pixelFormat == AVPixelFormat.AV_PIX_FMT_NONE)
            {
                return "no NV12 or YUV420P CPU input";
            }

            if (!candidate.SupportsSizeAndRate)
            {
                return "size or frame rate unsupported";
            }

            if (!candidate.SupportsBitRate)
            {
                return "target bitrate outside the supported range";
            }

            if (!candidate.SupportsBitRateMode)
            {
                return "bitrate mode unsupported";
            }

            return null;
        }

        /// <summary>Prefers NV12 input, which hardware encoders support most widely, over planar YUV420P.</summary>
        /// <param name="colorFormats">The component's declared Android input color formats.</param>
        /// <returns>The FFmpeg pixel format, or <see cref="AVPixelFormat.AV_PIX_FMT_NONE"/> when neither is declared.</returns>
        private static AVPixelFormat ChoosePixelFormat(IReadOnlyList<int> colorFormats)
        {
            var planar = false;
            for (var i = 0; i < colorFormats.Count; i++)
            {
                if (colorFormats[i] == ColorFormatYuv420SemiPlanar)
                {
                    return AVPixelFormat.AV_PIX_FMT_NV12;
                }

                planar |= colorFormats[i] == ColorFormatYuv420Planar;
            }

            return planar ? AVPixelFormat.AV_PIX_FMT_YUV420P : AVPixelFormat.AV_PIX_FMT_NONE;
        }
    }
}
