#nullable enable
using MajdataPlay.FFmpeg.Internal;
using System;
using System.Collections.Generic;
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Runtime.ExceptionServices;
using UnityEngine;
using AndroidBuild = MajdataPlay.Platform.Android.Runtime.Bindings.Android.Os.Build;
using JavaInteger = MajdataPlay.Platform.Android.Runtime.Bindings.Java.Lang.Integer;
using MediaCodecInfo = MajdataPlay.Platform.Android.Runtime.Bindings.Android.Media.MediaCodecInfo;
using MediaCodecList = MajdataPlay.Platform.Android.Runtime.Bindings.Android.Media.MediaCodecList;
#endif

namespace MajdataPlay.FFmpeg.Interop
{
    /// <summary>Reads Android's MediaCodec encoder capabilities for hardware component selection.</summary>
    internal static class AndroidMediaCodecEncoderQuery
    {
        /// <summary>Lists the regular encoder components that declare a MIME type, with capabilities for one recording.</summary>
        /// <param name="mimeType">The MediaCodec MIME type to match.</param>
        /// <param name="width">The coded width FFmpeg will configure.</param>
        /// <param name="height">The coded height FFmpeg will configure.</param>
        /// <param name="frameRate">The output frame rate.</param>
        /// <param name="bitRate">The requested target bitrate in bits per second.</param>
        /// <param name="constantBitRate">Whether CBR support is required instead of VBR support.</param>
        /// <returns>The matching candidates in <c>MediaCodecList</c> order.</returns>
        /// <remarks>Runs within scoped JNI thread attachment, so the encoder worker thread may call it.</remarks>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="NotSupportedException">The Android API level cannot classify hardware components.</exception>
        /// <exception cref="UnityEngine.AndroidJavaException">A Java call fails.</exception>
        /// <exception cref="MajdataPlay.Platform.Android.Runtime.JavaInvocationException">A generated Java wrapper invocation fails.</exception>
        /// <exception cref="InvalidOperationException">JNI cannot allocate a frame or resolve a required reference.</exception>
        internal static IReadOnlyList<MediaCodecEncoderCandidate> GetCandidates(string mimeType, int width, int height, int frameRate,
            int bitRate, bool constantBitRate)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            List<MediaCodecEncoderCandidate>? candidates = null;
            ExceptionDispatchInfo? failure = null;
            AndroidJNI.InvokeAttached(() =>
            {
                try
                {
                    candidates = ReadCandidates(mimeType, width, height, frameRate, bitRate, constantBitRate);
                }
                catch (Exception exception)
                {
                    // Do not allow managed exceptions to cross the native attachment callback.
                    failure = ExceptionDispatchInfo.Capture(exception);
                }
            });
            failure?.Throw();
            return candidates!;
#else
            throw new PlatformNotSupportedException("MediaCodec encoder queries require an Android player.");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>Reads candidates on a JVM-attached thread.</summary>
        /// <param name="mimeType">The MediaCodec MIME type to match.</param>
        /// <param name="width">The coded width FFmpeg will configure.</param>
        /// <param name="height">The coded height FFmpeg will configure.</param>
        /// <param name="frameRate">The output frame rate.</param>
        /// <param name="bitRate">The requested target bitrate in bits per second.</param>
        /// <param name="constantBitRate">Whether CBR support is required instead of VBR support.</param>
        /// <returns>The matching candidates in <c>MediaCodecList</c> order.</returns>
        /// <exception cref="NotSupportedException">The Android API level cannot classify hardware components.</exception>
        /// <exception cref="AndroidJavaException">A Java call fails.</exception>
        /// <exception cref="MajdataPlay.Platform.Android.Runtime.JavaInvocationException">A generated Java wrapper invocation fails.</exception>
        private static List<MediaCodecEncoderCandidate> ReadCandidates(string mimeType, int width, int height, int frameRate,
            int bitRate, bool constantBitRate)
        {
            var apiLevel = AndroidBuild.Version.SdkInt;
            if (apiLevel < MediaCodecEncoderSelector.MinimumHardwareClassificationApiLevel)
            {
                throw new NotSupportedException("Android API " + apiLevel + " cannot verify hardware-only MediaCodec components; API "
                    + MediaCodecEncoderSelector.MinimumHardwareClassificationApiLevel + " or later is required.");
            }

            var mode = constantBitRate ? MediaCodecInfo.EncoderCapabilities.BitrateModeCbr : MediaCodecInfo.EncoderCapabilities.BitrateModeVbr;
            var candidates = new List<MediaCodecEncoderCandidate>();
            using var list = new MediaCodecList(MediaCodecList.RegularCodecs);
            var infos = list.GetCodecInfos() ?? Array.Empty<MediaCodecInfo>();
            try
            {
                foreach (var info in infos)
                {
                    if (info == null || !info.IsEncoder())
                    {
                        continue;
                    }

                    var name = info.GetName();
                    var type = FindType(info.GetSupportedTypes(), mimeType);
                    if (string.IsNullOrEmpty(name) || type == null)
                    {
                        continue;
                    }

                    candidates.Add(ReadCandidate(info, name!, type, width, height, frameRate, bitRate, mode));
                }
            }
            finally
            {
                foreach (var info in infos)
                {
                    info?.Dispose();
                }
            }

            return candidates;
        }

        /// <summary>Reads one component's capabilities for the matched MIME type.</summary>
        /// <param name="info">The borrowed encoder component description.</param>
        /// <param name="name">The exact component name reported by Android.</param>
        /// <param name="type">The component's spelling of the matched MIME type.</param>
        /// <param name="width">The coded width FFmpeg will configure.</param>
        /// <param name="height">The coded height FFmpeg will configure.</param>
        /// <param name="frameRate">The output frame rate.</param>
        /// <param name="bitRate">The requested target bitrate in bits per second.</param>
        /// <param name="mode">The required Android bitrate-mode constant.</param>
        /// <returns>The candidate description; missing video or encoder capabilities are reported as unsupported.</returns>
        /// <exception cref="AndroidJavaException">A Java call fails.</exception>
        /// <exception cref="MajdataPlay.Platform.Android.Runtime.JavaInvocationException">A generated Java wrapper invocation fails.</exception>
        private static MediaCodecEncoderCandidate ReadCandidate(MediaCodecInfo info, string name, string type, int width, int height, int frameRate,
            int bitRate, int mode)
        {
            using var capabilities = info.GetCapabilitiesForType(type);
            using var video = capabilities?.GetVideoCapabilities();
            using var encoder = capabilities?.GetEncoderCapabilities();
            var supportsBitRate = false;
            if (video != null)
            {
                using var range = video.GetBitrateRange();
                using var requested = JavaInteger.ValueOf(bitRate);
                supportsBitRate = range != null && range.Contains(requested);
            }

            return new MediaCodecEncoderCandidate(name, info.IsAlias(), info.IsHardwareAccelerated(), info.IsSoftwareOnly(),
                capabilities?.ColorFormats ?? Array.Empty<int>(),
                video != null && video.AreSizeAndRateSupported(width, height, frameRate),
                supportsBitRate,
                encoder != null && encoder.IsBitrateModeSupported(mode));
        }

        /// <summary>Finds a supported type matching the requested MIME type without regard to case.</summary>
        /// <param name="types">The component's supported types, or null when Java returns none.</param>
        /// <param name="mimeType">The requested MIME type.</param>
        /// <returns>The component's spelling of the type, or null when it is not supported.</returns>
        private static string? FindType(string?[]? types, string mimeType)
        {
            if (types == null)
            {
                return null;
            }

            foreach (var type in types)
            {
                if (string.Equals(type, mimeType, StringComparison.OrdinalIgnoreCase))
                {
                    return type;
                }
            }

            return null;
        }
#endif
    }
}
