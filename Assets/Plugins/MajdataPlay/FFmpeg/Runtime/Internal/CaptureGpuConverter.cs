#nullable enable
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MajdataPlay.FFmpeg.Internal
{
    /// <summary>Converts scaled camera captures to the encoder's limited-range BT.709 NV12 input on the GPU before readback.</summary>
    /// <remarks>
    /// The readback then carries 1.5 instead of 4 bytes per pixel, and the encoder worker copies planes instead of converting
    /// RGBA on CPU threads. The shader applies the fixed-point weights of <see cref="VideoPixelKernels"/> to the same 8-bit
    /// codes, so its samples equal those of <see cref="CaptureFrameConverter"/>. One scaled RGBA target is shared by every
    /// capture because each conversion reads it before the next capture overwrites it in GPU order. Create, record, and
    /// dispose on the Unity main thread.
    /// </remarks>
    internal sealed class CaptureGpuConverter : IDisposable
    {
        /// <summary>Names the conversion shader under a Resources folder of this assembly.</summary>
        private const string ShaderResource = "FFmpegCaptureNv12";
        /// <summary>Identifies the frame size and row order vector.</summary>
        private static readonly int LayoutProperty = Shader.PropertyToID("_FFCLayout");
        /// <summary>Identifies the luma weights.</summary>
        private static readonly int LumaProperty = Shader.PropertyToID("_FFCLuma");
        /// <summary>Identifies the blue-difference weights.</summary>
        private static readonly int BlueProperty = Shader.PropertyToID("_FFCBlue");
        /// <summary>Identifies the red-difference weights.</summary>
        private static readonly int RedProperty = Shader.PropertyToID("_FFCRed");
        /// <summary>Identifies the fixed-point fraction bits.</summary>
        private static readonly int ShiftsProperty = Shader.PropertyToID("_FFCShifts");
        /// <summary>Owns the configured conversion material.</summary>
        private readonly Material _material;
        /// <summary>Owns the scaled RGBA copy of the camera output that the conversion reads.</summary>
        private readonly RenderTexture _scaled;
        /// <summary>Stores the fixed frame width in pixels.</summary>
        private readonly int _width;
        /// <summary>Stores the fixed frame height in pixels.</summary>
        private readonly int _height;
        /// <summary>Prevents releasing the GPU objects twice.</summary>
        private bool _disposed;

        /// <summary>Creates the material and scaled target for one recording size.</summary>
        /// <param name="shader">The supported conversion shader.</param>
        /// <param name="scaled">The created scaled RGBA target, owned by the converter.</param>
        /// <param name="width">The even frame width in pixels.</param>
        /// <param name="height">The even frame height in pixels.</param>
        /// <param name="flipVertically">Whether the scaled target's rows are bottom-first.</param>
        private CaptureGpuConverter(Shader shader, RenderTexture scaled, int width, int height, bool flipVertically)
        {
            _scaled = scaled;
            _width = width;
            _height = height;
            _material = new Material(shader)
            {
                name = "FFmpeg capture NV12 conversion",
                hideFlags = HideFlags.HideAndDontSave
            };
            // Every weight and offset is an integer below 2^26 with few significant bits, so the floats carry them exactly.
            _material.SetVector(LayoutProperty, new Vector4(width, height, flipVertically ? 1f : 0f, 0f));
            _material.SetVector(LumaProperty, new Vector4(VideoPixelKernels.LumaRed, VideoPixelKernels.LumaGreen,
                VideoPixelKernels.LumaBlue, VideoPixelKernels.LumaOffset));
            _material.SetVector(BlueProperty, new Vector4(VideoPixelKernels.BlueDifferenceRed, VideoPixelKernels.BlueDifferenceGreen,
                VideoPixelKernels.BlueDifferenceBlue, VideoPixelKernels.ChromaOffset));
            _material.SetVector(RedProperty, new Vector4(VideoPixelKernels.RedDifferenceRed, VideoPixelKernels.RedDifferenceGreen,
                VideoPixelKernels.RedDifferenceBlue, VideoPixelKernels.ChromaOffset));
            _material.SetVector(ShiftsProperty, new Vector4(VideoPixelKernels.FixedPointBits, VideoPixelKernels.ChromaBits, 0f, 0f));
        }

        /// <summary>Gets the texture format of the readback targets and their requests.</summary>
        internal static TextureFormat ReadbackFormat => TextureFormat.R8;

        /// <summary>Creates a converter when the device can run the shader and render and read back 8-bit single-channel targets.</summary>
        /// <param name="width">The even frame width in pixels.</param>
        /// <param name="height">The even frame height in pixels.</param>
        /// <param name="flipVertically">Whether the scaled capture rows are bottom-first and must be reversed.</param>
        /// <param name="unavailableReason">Receives why GPU conversion is unavailable, or null when a converter is returned.</param>
        /// <returns>The converter, or null when the CPU conversion must be used.</returns>
        internal static CaptureGpuConverter? TryCreate(int width, int height, bool flipVertically, out string? unavailableReason)
        {
            if (width < 2 || (width & 1) != 0 || height < 2 || (height & 1) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "The frame dimensions must be positive and even.");
            }

            if (Math.Max(width, height + (height / 2)) > SystemInfo.maxTextureSize)
            {
                unavailableReason = "the NV12 target exceeds the maximum texture size";
                return null;
            }

            if (!SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Render)
                || !SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.ReadPixels))
            {
                unavailableReason = "the device cannot render and read back R8 targets";
                return null;
            }

            var shader = Resources.Load<Shader>(ShaderResource);
            if (shader == null || !shader.isSupported)
            {
                unavailableReason = "the NV12 conversion shader is unavailable on this device";
                return null;
            }

            var scaled = CreateRgbaTarget(width, height);
            if (scaled == null)
            {
                unavailableReason = "the scaled RGBA target cannot be created";
                return null;
            }

            unavailableReason = null;
            return new CaptureGpuConverter(shader, scaled, width, height, flipVertically);
        }

        /// <summary>Creates an RGBA8 capture target matching the camera output's sRGB encoding.</summary>
        /// <param name="width">The target width in pixels.</param>
        /// <param name="height">The target height in pixels.</param>
        /// <returns>The created target, or null when the device cannot create it.</returns>
        internal static RenderTexture? CreateRgbaTarget(int width, int height)
        {
            var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "FFmpeg camera readback",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };

            if (!texture.Create())
            {
                Object.Destroy(texture);
                return null;
            }

            return texture;
        }

        /// <summary>Creates one NV12 readback target: <c>height * 3 / 2</c> rows of <c>width</c> single bytes.</summary>
        /// <returns>The created target, owned by the caller.</returns>
        /// <exception cref="NotSupportedException">The device cannot create the target.</exception>
        internal RenderTexture CreateTarget()
        {
            var texture = new RenderTexture(_width, _height + (_height / 2), 0, GraphicsFormat.R8_UNorm)
            {
                name = "FFmpeg camera NV12 readback",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Point
            };

            if (!texture.Create())
            {
                Object.Destroy(texture);
                throw new NotSupportedException("Cannot create the camera's NV12 capture target.");
            }

            return texture;
        }

        /// <summary>Records the scaled copy of the camera output and its NV12 conversion.</summary>
        /// <param name="commands">The command buffer that executes after the camera renders.</param>
        /// <param name="source">The final camera color target.</param>
        /// <param name="scale">The normalized size of the camera region within the source.</param>
        /// <param name="offset">The normalized origin of the camera region within the source.</param>
        /// <param name="target">A target from <see cref="CreateTarget"/> that receives the NV12 frame.</param>
        /// <exception cref="ObjectDisposedException">The converter has been disposed.</exception>
        internal void Record(CommandBuffer commands, RenderTargetIdentifier source, Vector2 scale, Vector2 offset, RenderTexture target)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CaptureGpuConverter));
            }

            // The first copy is the RGBA capture of the CPU path, so both paths convert the same scaled pixels.
            commands.Blit(source, _scaled, scale, offset);
            commands.Blit(_scaled, target, _material, 0);
        }

        /// <summary>Destroys the material and the scaled target; repeated calls have no effect.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _scaled.Release();
            Object.Destroy(_scaled);
            Object.Destroy(_material);
        }
    }
}
