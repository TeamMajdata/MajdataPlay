using System;
using System.Text;
using FFmpeg.AutoGen;
using MajdataPlay.FFmpeg;
using MajdataPlay.FFmpeg.Internal;
using MajdataPlay.FFmpeg.Interop;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies Android MediaCodec hardware component selection without requiring a device.</summary>
    public class MediaCodecEncoderSelectorTests
    {
        /// <summary>Creates a candidate that satisfies every requirement unless overridden.</summary>
        /// <param name="name">The component name.</param>
        /// <param name="isAlias">Whether the component is an alias.</param>
        /// <param name="hardware">Whether Android reports hardware acceleration.</param>
        /// <param name="softwareOnly">Whether Android reports a software-only implementation.</param>
        /// <param name="colorFormats">The declared input color formats, or null for NV12 and YUV420P.</param>
        /// <param name="sizeAndRate">Whether size and frame rate are supported.</param>
        /// <param name="bitRate">Whether the target bitrate is supported.</param>
        /// <param name="bitRateMode">Whether the bitrate mode is supported.</param>
        /// <returns>The candidate description.</returns>
        private static MediaCodecEncoderCandidate Candidate(string name = "c2.vendor.avc.encoder", bool isAlias = false, bool hardware = true,
            bool softwareOnly = false, int[]? colorFormats = null, bool sizeAndRate = true, bool bitRate = true, bool bitRateMode = true)
        {
            colorFormats ??= new[] { MediaCodecEncoderSelector.ColorFormatYuv420Planar, MediaCodecEncoderSelector.ColorFormatYuv420SemiPlanar };
            return new MediaCodecEncoderCandidate(name, isAlias, hardware, softwareOnly, colorFormats, sizeAndRate, bitRate, bitRateMode);
        }

        /// <summary>Checks that each MediaCodec-backed format maps to the matching FFmpeg wrapper and MIME type.</summary>
        /// <param name="format">The requested compressed format.</param>
        /// <param name="encoder">The expected FFmpeg wrapper name.</param>
        /// <param name="mimeType">The expected MediaCodec MIME type.</param>
        [TestCase(VideoEncodingFormat.H264, "h264_mediacodec", "video/avc")]
        [TestCase(VideoEncodingFormat.HEVC, "hevc_mediacodec", "video/hevc")]
        [TestCase(VideoEncodingFormat.VP9, "vp9_mediacodec", "video/x-vnd.on2.vp9")]
        [TestCase(VideoEncodingFormat.AV1, "av1_mediacodec", "video/av01")]
        public void FormatMapping_UsesMatchingWrapperAndMime(VideoEncodingFormat format, string encoder, string mimeType)
        {
            Assert.That(MediaCodecEncoderSelector.GetEncoderName(format), Is.EqualTo(encoder));
            Assert.That(MediaCodecEncoderSelector.GetMimeType(format), Is.EqualTo(mimeType));
        }

        /// <summary>Checks that MPEG-4 keeps using FFmpeg's software encoder and never a MediaCodec wrapper.</summary>
        [Test]
        public void FormatMapping_Mpeg4_HasNoWrapper()
        {
            Assert.That(MediaCodecEncoderSelector.GetEncoderName(VideoEncodingFormat.MPEG4), Is.Null);
            Assert.That(MediaCodecEncoderSelector.GetMimeType(VideoEncodingFormat.MPEG4), Is.Null);
        }

        /// <summary>Checks that capability queries use the 16-aligned size FFmpeg configures for H.264 and HEVC.</summary>
        /// <param name="format">The requested compressed format.</param>
        /// <param name="width">The expected coded width for 1920x1080.</param>
        /// <param name="height">The expected coded height for 1920x1080.</param>
        [TestCase(VideoEncodingFormat.H264, 1920, 1088)]
        [TestCase(VideoEncodingFormat.HEVC, 1920, 1088)]
        [TestCase(VideoEncodingFormat.VP9, 1920, 1080)]
        [TestCase(VideoEncodingFormat.AV1, 1920, 1080)]
        public void ConfiguredSize_MatchesFFmpegPadding(VideoEncodingFormat format, int width, int height)
        {
            Assert.That(MediaCodecEncoderSelector.GetConfiguredSize(format, 1920, 1080), Is.EqualTo((width, height)));
        }

        /// <summary>Checks that a fully capable hardware component is selected with NV12 input.</summary>
        [Test]
        public void TrySelect_HardwareComponent_PrefersNv12()
        {
            var failures = new StringBuilder();

            var selected = MediaCodecEncoderSelector.TrySelect(new[] { Candidate() }, out var selection, failures);

            Assert.That(selected, Is.True);
            Assert.That(selection.ComponentName, Is.EqualTo("c2.vendor.avc.encoder"));
            Assert.That(selection.PixelFormat, Is.EqualTo(AVPixelFormat.AV_PIX_FMT_NV12));
            Assert.That(failures.ToString(), Is.Empty);
        }

        /// <summary>Checks that planar YUV420P is used only when the component declares no NV12 input.</summary>
        [Test]
        public void TrySelect_PlanarOnly_UsesYuv420P()
        {
            var candidate = Candidate(colorFormats: new[] { 0x7F000789, MediaCodecEncoderSelector.ColorFormatYuv420Planar });

            var selected = MediaCodecEncoderSelector.TrySelect(new[] { candidate }, out var selection, new StringBuilder());

            Assert.That(selected, Is.True);
            Assert.That(selection.PixelFormat, Is.EqualTo(AVPixelFormat.AV_PIX_FMT_YUV420P));
        }

        /// <summary>Checks that software, alias, and incapable components are skipped with a reason each.</summary>
        [Test]
        public void TrySelect_SkipsUnsuitableComponents_AndSelectsFirstSuitable()
        {
            var candidates = new[]
            {
                Candidate("c2.android.avc.encoder", hardware: false, softwareOnly: true),
                Candidate("c2.vendor.alias", isAlias: true),
                Candidate("c2.vendor.mixed", softwareOnly: true),
                Candidate("c2.vendor.surface", colorFormats: new[] { 0x7F000789 }),
                Candidate("c2.vendor.small", sizeAndRate: false),
                Candidate("c2.vendor.lowrate", bitRate: false),
                Candidate("c2.vendor.cqonly", bitRateMode: false),
                Candidate("c2.vendor.good"),
                Candidate("c2.vendor.later")
            };
            var failures = new StringBuilder();

            var selected = MediaCodecEncoderSelector.TrySelect(candidates, out var selection, failures);

            Assert.That(selected, Is.True);
            Assert.That(selection.ComponentName, Is.EqualTo("c2.vendor.good"));
            var text = failures.ToString();
            Assert.That(text, Does.Contain("c2.android.avc.encoder: not a hardware-only component"));
            Assert.That(text, Does.Contain("c2.vendor.alias: alias component"));
            Assert.That(text, Does.Contain("c2.vendor.mixed: not a hardware-only component"));
            Assert.That(text, Does.Contain("c2.vendor.surface: no NV12 or YUV420P CPU input"));
            Assert.That(text, Does.Contain("c2.vendor.small: size or frame rate unsupported"));
            Assert.That(text, Does.Contain("c2.vendor.lowrate: target bitrate outside the supported range"));
            Assert.That(text, Does.Contain("c2.vendor.cqonly: bitrate mode unsupported"));
            Assert.That(text, Does.Not.Contain("c2.vendor.later"));
        }

        /// <summary>Checks that only software components produce no selection, so recording falls back to software.</summary>
        [Test]
        public void TrySelect_OnlySoftwareComponents_ReturnsFalse()
        {
            var failures = new StringBuilder();

            var selected = MediaCodecEncoderSelector.TrySelect(new[] { Candidate("c2.android.avc.encoder", hardware: false, softwareOnly: true) },
                out var selection, failures);

            Assert.That(selected, Is.False);
            Assert.That(selection.ComponentName, Is.Null);
            Assert.That(failures.ToString(), Does.Contain("c2.android.avc.encoder: not a hardware-only component"));
        }

        /// <summary>Checks that an empty component list is reported instead of silently selecting nothing.</summary>
        [Test]
        public void TrySelect_NoComponents_ReportsReason()
        {
            var failures = new StringBuilder();

            var selected = MediaCodecEncoderSelector.TrySelect(Array.Empty<MediaCodecEncoderCandidate>(), out _, failures);

            Assert.That(selected, Is.False);
            Assert.That(failures.ToString(), Does.Contain("no encoder component"));
        }

        /// <summary>Checks argument validation and that candidates keep their own copy of color formats.</summary>
        [Test]
        public void Candidate_ValidatesAndCopiesColorFormats()
        {
            var formats = new[] { MediaCodecEncoderSelector.ColorFormatYuv420SemiPlanar };
            var candidate = Candidate(colorFormats: formats);
            formats[0] = 0;

            Assert.That(candidate.ColorFormats, Is.EqualTo(new[] { MediaCodecEncoderSelector.ColorFormatYuv420SemiPlanar }));
            Assert.Throws<ArgumentException>(() => Candidate(name: ""));
            Assert.Throws<ArgumentNullException>(() => new MediaCodecEncoderCandidate("x", false, true, false, null!, true, true, true));
            Assert.Throws<ArgumentNullException>(() => MediaCodecEncoderSelector.TrySelect(null!, out _, new StringBuilder()));
            Assert.Throws<ArgumentNullException>(() => MediaCodecEncoderSelector.TrySelect(new[] { candidate }, out _, null!));
        }

        /// <summary>Checks that the Android capability query is unavailable outside an Android player.</summary>
        [Test]
        public void AndroidQuery_OutsidePlayer_IsNotSupported()
        {
            Assert.Throws<PlatformNotSupportedException>(() =>
                AndroidMediaCodecEncoderQuery.GetCandidates("video/avc", 1920, 1088, 60, 8000000, false));
        }
    }
}
