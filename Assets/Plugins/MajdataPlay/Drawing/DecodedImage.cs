using SkiaSharp;
using System;
using System.Buffers;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Drawing
{
    /// <summary>
    /// Owns one decoded image as straight-alpha RGBA32 pixels in Unity's bottom-up row order, in native memory.
    /// Create it with <see cref="EncodedImage.Decode"/>; the pixels are exposed as a <see cref="NativeArray{T}"/> view
    /// so that jobs and textures read them without copying to the managed heap.
    /// </summary>
    public sealed class DecodedImage : IDisposable
    {
        /// <summary>
        /// Holds the native pixel storage.
        /// </summary>
        private readonly SKBitmap _bitmap;

        /// <summary>
        /// Holds the pixel view, created on first access.
        /// </summary>
        private NativeArray<Color32> _pixels;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        /// <summary>
        /// Holds the safety handle of the pixel view.
        /// </summary>
        private AtomicSafetyHandle _safety;
#endif

        /// <summary>
        /// Holds whether the pixel view has been created.
        /// </summary>
        private bool _hasPixelView;

        /// <summary>
        /// Holds whether the native storage has been released.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Gets the image width in pixels.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the image height in pixels.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets a view of the pixels, row 0 being the bottom row. The view is valid until this image is disposed.
        /// Access it on the main thread.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The image has been disposed.</exception>
        public NativeArray<Color32> Pixels
        {
            get
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(DecodedImage));
                }
                if (!_hasPixelView)
                {
                    var span = MemoryMarshal.Cast<byte, Color32>(_bitmap.GetPixelSpan());
                    _pixels = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray(span, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    _safety = AtomicSafetyHandle.Create();
                    NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref _pixels, _safety);
#endif
                    _hasPixelView = true;
                }
                return _pixels;
            }
        }

        /// <summary>
        /// Wraps decoded storage.
        /// </summary>
        /// <param name="bitmap">The RGBA8888 storage, already flipped to bottom-up rows.</param>
        private DecodedImage(SKBitmap bitmap)
        {
            _bitmap = bitmap;
            Width = bitmap.Width;
            Height = bitmap.Height;
        }

        /// <summary>
        /// Decodes a codec's image straight into RGBA32 storage, allocating only the decoded pixels.
        /// Safe to call off the main thread.
        /// </summary>
        /// <param name="codec">The codec positioned at the image.</param>
        /// <returns>The decoded image, or null when the data is corrupt or the image is too large for one buffer.</returns>
        internal static DecodedImage? Decode(SKCodec codec)
        {
            var encoded = codec.Info;
            // Straight alpha and no colour-space conversion, matching SKBitmapExtensions.ToTexture2D's output.
            var info = new SKImageInfo(encoded.Width, encoded.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            if (info.Width <= 0 || info.Height <= 0 || (long)info.RowBytes * info.Height > int.MaxValue)
            {
                return null;
            }

            var bitmap = new SKBitmap();
            try
            {
                if (!bitmap.TryAllocPixels(info))
                {
                    bitmap.Dispose();
                    return null;
                }
                var result = codec.GetPixels(info, bitmap.GetPixels());
                if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                {
                    bitmap.Dispose();
                    return null;
                }
                FlipRows(bitmap.GetPixelSpan(), bitmap.RowBytes, info.Height);
                return new DecodedImage(bitmap);
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Creates a trilinear, clamped RGBA32 texture with a full mipmap chain from the pixels, copying them
        /// natively. Call on the main thread.
        /// </summary>
        /// <param name="markNonReadable">Whether to release the texture's CPU copy after upload.</param>
        /// <returns>The uploaded texture.</returns>
        /// <exception cref="ObjectDisposedException">The image has been disposed.</exception>
        public Texture2D ToTexture2D(bool markNonReadable)
        {
            var pixels = Pixels;
            // Skip Unity's own initialization: the base level is overwritten and Apply regenerates the mipmaps.
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, -1, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear
            };
            try
            {
                texture.GetPixelData<Color32>(0).CopyFrom(pixels);
                texture.Apply(updateMipmaps: true, makeNoLongerReadable: markNonReadable);
                return texture;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
        }

        /// <summary>
        /// Reverses the row order in place, converting Skia's top-down rows to Unity's bottom-up rows.
        /// </summary>
        /// <param name="pixels">The native pixel storage.</param>
        /// <param name="rowBytes">The bytes per row.</param>
        /// <param name="height">The number of rows.</param>
        private static void FlipRows(Span<byte> pixels, int rowBytes, int height)
        {
            // One pooled row of scratch; the pixel data itself stays in native memory.
            var scratch = ArrayPool<byte>.Shared.Rent(rowBytes);
            try
            {
                var temporary = scratch.AsSpan(0, rowBytes);
                for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
                {
                    var upper = pixels.Slice(top * rowBytes, rowBytes);
                    var lower = pixels.Slice(bottom * rowBytes, rowBytes);
                    upper.CopyTo(temporary);
                    lower.CopyTo(upper);
                    temporary.CopyTo(lower);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        }

        /// <summary>
        /// Invalidates the pixel view and releases the native storage.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (_hasPixelView)
            {
                AtomicSafetyHandle.CheckDeallocateAndThrow(_safety);
                AtomicSafetyHandle.Release(_safety);
            }
#endif
            _pixels = default;
            _bitmap.Dispose();
        }
    }
}
