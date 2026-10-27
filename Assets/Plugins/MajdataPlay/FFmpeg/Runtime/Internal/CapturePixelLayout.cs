#nullable enable
using System;

namespace MajdataPlay.FFmpeg.Internal
{
    /// <summary>Identifies the tightly packed pixel layout that a capture readback delivers to the encoder worker.</summary>
    internal enum CapturePixelLayout
    {
        /// <summary>sRGB RGBA8 pixels in readback row order, converted to YUV on the encoder worker.</summary>
        Rgba32,
        /// <summary>Limited-range BT.709 NV12 converted on the GPU: the Y plane followed by interleaved Cb/Cr rows, top row first.</summary>
        Nv12
    }

    /// <summary>Provides the buffer sizes of <see cref="CapturePixelLayout"/> values.</summary>
    internal static class CapturePixelLayoutExtensions
    {
        /// <summary>Gets the byte count of one tightly packed frame.</summary>
        /// <param name="layout">The frame layout.</param>
        /// <param name="width">The even frame width in pixels.</param>
        /// <param name="height">The even frame height in pixels.</param>
        /// <returns>Four bytes per pixel for RGBA32, or one and a half for NV12.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The layout is not defined.</exception>
        /// <exception cref="OverflowException">The byte count exceeds <see cref="int.MaxValue"/>.</exception>
        internal static int GetFrameByteCount(this CapturePixelLayout layout, int width, int height)
        {
            return layout switch
            {
                CapturePixelLayout.Rgba32 => checked(width * height * 4),
                CapturePixelLayout.Nv12 => checked(width * height * 3 / 2),
                _ => throw new ArgumentOutOfRangeException(nameof(layout))
            };
        }
    }
}
