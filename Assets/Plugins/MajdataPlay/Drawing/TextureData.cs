using SkiaSharp;
using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Drawing
{
    /// <summary>
    /// Owns the pixels of one texture prepared for upload: straight-alpha RGBA32 in Unity's bottom-up row order,
    /// optionally followed by a box-filtered mipmap chain, in native memory. Creating it calls no Unity API, so
    /// decoding, conversion and mipmap generation can run on any thread; only <see cref="ToTexture2D"/> needs the
    /// main thread.
    /// </summary>
    /// <remarks>
    /// Instances are not thread-safe: hand one to the main thread only after the creating thread is done with it.
    /// </remarks>
    public sealed class TextureData : IDisposable
    {
        /// <summary>
        /// The size of one RGBA32 pixel.
        /// </summary>
        const int BYTES_PER_PIXEL = 4;

        /// <summary>
        /// Holds the base level followed by the smaller mipmap levels, each in bottom-up row order.
        /// </summary>
        NativeArray<byte> _pixels;

        /// <summary>
        /// Holds whether the native storage has been released.
        /// </summary>
        bool _disposed;

        /// <summary>
        /// Gets the width of the base level in pixels.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the height of the base level in pixels.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets the number of levels stored, including the base level.
        /// </summary>
        public int MipCount { get; }

        /// <summary>
        /// Gets whether a full mipmap chain was requested. A 1x1 image has a chain of one level.
        /// </summary>
        public bool HasMipChain { get; }

        /// <summary>
        /// Allocates uninitialized storage for an image and its optional mipmap chain.
        /// </summary>
        /// <param name="width">The base level width.</param>
        /// <param name="height">The base level height.</param>
        /// <param name="generateMipmaps">Whether to reserve a full mipmap chain.</param>
        /// <exception cref="ArgumentOutOfRangeException">The image does not fit in one buffer.</exception>
        TextureData(int width, int height, bool generateMipmaps)
        {
            Width = width;
            Height = height;
            HasMipChain = generateMipmaps;
            MipCount = generateMipmaps ? ComputeMipCount(width, height) : 1;
            var totalBytes = ComputeTotalBytes(width, height, MipCount);
            if (totalBytes > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(width), $"A {width}x{height} texture does not fit in one buffer.");
            }
            _pixels = new NativeArray<byte>((int)totalBytes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        /// <summary>
        /// Decodes an encoded image (PNG, JPEG, WebP, ...) straight into RGBA32 and builds the requested levels.
        /// Safe to call off the main thread.
        /// </summary>
        /// <param name="encoded">The encoded image bytes.</param>
        /// <param name="generateMipmaps">Whether to build a full box-filtered mipmap chain.</param>
        /// <returns>The prepared pixels, owned by the caller.</returns>
        /// <exception cref="ArgumentException">The bytes are empty, not a supported image or corrupt.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The image does not fit in one buffer.</exception>
        /// <exception cref="OutOfMemoryException">The decoded pixels could not be allocated.</exception>
        public static TextureData Decode(ReadOnlySpan<byte> encoded, bool generateMipmaps)
        {
            if (encoded.IsEmpty)
            {
                throw new ArgumentException("The encoded image is empty.", nameof(encoded));
            }
            // SKCodec and SKBitmap skip SkiaSharp's handle registry, so only this SKData takes its lock.
            using var data = SKData.CreateCopy(encoded);
            using var codec = SKCodec.Create(data) ?? throw new ArgumentException("The data is not an image format Skia can decode.", nameof(encoded));
            var codecInfo = codec.Info;
            ThrowIfInvalidSize(codecInfo.Width, codecInfo.Height, nameof(encoded));
            // Straight alpha and no colour-space conversion, as DecodedImage does.
            var info = new SKImageInfo(codecInfo.Width, codecInfo.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var bitmap = new SKBitmap();
            if (!bitmap.TryAllocPixels(info))
            {
                throw new OutOfMemoryException($"Could not allocate a {info.Width}x{info.Height} bitmap.");
            }
            var result = codec.GetPixels(info, bitmap.GetPixels());
            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            {
                throw new ArgumentException($"The image could not be decoded: {result}.", nameof(encoded));
            }

            return FromRgbaPixels(bitmap.GetPixelSpan(), bitmap.RowBytes, info.Width, info.Height, generateMipmaps);
        }

        /// <summary>
        /// Converts a bitmap to RGBA32 and builds the requested levels. Safe to call off the main thread as long as no
        /// other thread uses the bitmap.
        /// </summary>
        /// <remarks>
        /// RGBA8888 bitmaps with straight or no alpha are copied directly. Other layouts are converted by Skia, which
        /// needs two pixmaps registered in SkiaSharp's handle registry.
        /// </remarks>
        /// <param name="bitmap">The source bitmap in Skia's top-down row order.</param>
        /// <param name="generateMipmaps">Whether to build a full box-filtered mipmap chain.</param>
        /// <returns>The prepared pixels, owned by the caller.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="bitmap"/> is null.</exception>
        /// <exception cref="ArgumentException">The bitmap is empty, has no pixels or cannot be converted.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The image does not fit in one buffer.</exception>
        /// <exception cref="OutOfMemoryException">The converted pixels could not be allocated.</exception>
        public static TextureData FromBitmap(SKBitmap bitmap, bool generateMipmaps)
        {
            if (bitmap is null)
            {
                throw new ArgumentNullException(nameof(bitmap));
            }
            var width = bitmap.Width;
            var height = bitmap.Height;
            ThrowIfInvalidSize(width, height, nameof(bitmap));
            if (bitmap.ColorType == SKColorType.Rgba8888 && bitmap.AlphaType is SKAlphaType.Unpremul or SKAlphaType.Opaque)
            {
                return FromRgbaPixels(bitmap.GetPixelSpan(), bitmap.RowBytes, width, height, generateMipmaps);
            }

            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var converted = new SKBitmap();
            if (!converted.TryAllocPixels(info))
            {
                throw new OutOfMemoryException($"Could not allocate a {width}x{height} bitmap.");
            }
            using (var source = bitmap.PeekPixels() ?? throw new ArgumentException("The bitmap has no pixels.", nameof(bitmap)))
            using (var destination = converted.PeekPixels())
            {
                if (!source.ReadPixels(destination))
                {
                    throw new ArgumentException($"A {bitmap.ColorType} bitmap cannot be converted to RGBA32.", nameof(bitmap));
                }
            }

            return FromRgbaPixels(converted.GetPixelSpan(), converted.RowBytes, width, height, generateMipmaps);
        }

        /// <summary>
        /// Creates a clamped RGBA32 texture from the prepared levels, trilinear when it has a mipmap chain and bilinear
        /// otherwise. Only copies and uploads; call on the main thread.
        /// </summary>
        /// <param name="markNonReadable">Whether to release the texture's CPU copy after upload.</param>
        /// <returns>The uploaded texture.</returns>
        /// <exception cref="ObjectDisposedException">The data has been disposed.</exception>
        /// <exception cref="InvalidOperationException">Unity created a different number of levels than were prepared.</exception>
        public Texture2D ToTexture2D(bool markNonReadable)
        {
            ThrowIfDisposed();
            // The prepared bytes overwrite every level, so skip Unity's own initialization.
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, MipCount, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = HasMipChain ? FilterMode.Trilinear : FilterMode.Bilinear
            };
            try
            {
                if (texture.mipmapCount != MipCount)
                {
                    throw new InvalidOperationException($"Unity created {texture.mipmapCount} levels for a {Width}x{Height} texture; {MipCount} were prepared.");
                }
                texture.LoadRawTextureData(_pixels);
                texture.Apply(false, markNonReadable);
                return texture;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
        }

        /// <summary>
        /// Releases the native storage. Safe to call more than once and from any thread.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _pixels.Dispose();
        }

        /// <summary>
        /// Computes the number of levels in a full mipmap chain, the same way Unity sizes one.
        /// </summary>
        /// <param name="width">The base level width.</param>
        /// <param name="height">The base level height.</param>
        /// <returns>The number of levels down to 1x1, including the base level.</returns>
        internal static int ComputeMipCount(int width, int height)
        {
            var count = 1;
            while (width > 1 || height > 1)
            {
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
                count++;
            }

            return count;
        }

        /// <summary>
        /// Computes the bytes of the base level and the following levels.
        /// </summary>
        /// <param name="width">The base level width.</param>
        /// <param name="height">The base level height.</param>
        /// <param name="mipCount">The number of levels, including the base level.</param>
        /// <returns>The total byte count.</returns>
        static long ComputeTotalBytes(int width, int height, int mipCount)
        {
            long total = 0;
            for (var mip = 0; mip < mipCount; mip++)
            {
                total += (long)width * height * BYTES_PER_PIXEL;
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
            }

            return total;
        }

        /// <summary>
        /// Copies top-down RGBA32 rows into a new instance, flipping them to bottom-up, and builds the mipmap chain.
        /// </summary>
        /// <param name="source">The source pixels.</param>
        /// <param name="rowBytes">The bytes per source row, which may include padding.</param>
        /// <param name="width">The width in pixels.</param>
        /// <param name="height">The height in pixels.</param>
        /// <param name="generateMipmaps">Whether to build a full mipmap chain.</param>
        /// <returns>The prepared pixels, owned by the caller.</returns>
        /// <exception cref="ArgumentException">The source holds fewer rows than <paramref name="height"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The image does not fit in one buffer.</exception>
        static TextureData FromRgbaPixels(ReadOnlySpan<byte> source, int rowBytes, int width, int height, bool generateMipmaps)
        {
            var packedRowBytes = width * BYTES_PER_PIXEL;
            if (rowBytes < packedRowBytes || source.Length < ((long)rowBytes * (height - 1)) + packedRowBytes)
            {
                throw new ArgumentException("The bitmap has no pixels or fewer rows than its height.", nameof(source));
            }
            var data = new TextureData(width, height, generateMipmaps);
            try
            {
                var destination = data._pixels.AsSpan();
                for (var y = 0; y < height; y++)
                {
                    source.Slice(y * rowBytes, packedRowBytes)
                          .CopyTo(destination.Slice((height - 1 - y) * packedRowBytes, packedRowBytes));
                }
                data.BuildMipChain();
                return data;
            }
            catch
            {
                data.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Fills every level after the base level with the 2x2 box filter of the level above it. Odd edges reuse their
        /// last row or column.
        /// </summary>
        void BuildMipChain()
        {
            var pixels = MemoryMarshal.Cast<byte, Color32>(_pixels.AsSpan());
            var sourceOffset = 0;
            var sourceWidth = Width;
            var sourceHeight = Height;
            for (var mip = 1; mip < MipCount; mip++)
            {
                var sourceLength = sourceWidth * sourceHeight;
                var levelWidth = Math.Max(1, sourceWidth / 2);
                var levelHeight = Math.Max(1, sourceHeight / 2);
                var source = pixels.Slice(sourceOffset, sourceLength);
                var level = pixels.Slice(sourceOffset + sourceLength, levelWidth * levelHeight);
                Downsample(source, sourceWidth, sourceHeight, level, levelWidth, levelHeight);
                sourceOffset += sourceLength;
                sourceWidth = levelWidth;
                sourceHeight = levelHeight;
            }
        }

        /// <summary>
        /// Box-filters one level into the next smaller one.
        /// </summary>
        /// <param name="source">The source level.</param>
        /// <param name="sourceWidth">The source width.</param>
        /// <param name="sourceHeight">The source height.</param>
        /// <param name="destination">The destination level.</param>
        /// <param name="width">The destination width.</param>
        /// <param name="height">The destination height.</param>
        static void Downsample(ReadOnlySpan<Color32> source, int sourceWidth, int sourceHeight, Span<Color32> destination, int width, int height)
        {
            for (var y = 0; y < height; y++)
            {
                var upper = source.Slice(y * 2 * sourceWidth, sourceWidth);
                var lower = source.Slice(Math.Min((y * 2) + 1, sourceHeight - 1) * sourceWidth, sourceWidth);
                var row = destination.Slice(y * width, width);
                for (var x = 0; x < width; x++)
                {
                    var left = x * 2;
                    var right = Math.Min(left + 1, sourceWidth - 1);
                    var c00 = upper[left];
                    var c10 = upper[right];
                    var c01 = lower[left];
                    var c11 = lower[right];
                    row[x] = new Color32(
                        (byte)((c00.r + c10.r + c01.r + c11.r) >> 2),
                        (byte)((c00.g + c10.g + c01.g + c11.g) >> 2),
                        (byte)((c00.b + c10.b + c01.b + c11.b) >> 2),
                        (byte)((c00.a + c10.a + c01.a + c11.a) >> 2));
                }
            }
        }

        /// <summary>
        /// Validates image dimensions.
        /// </summary>
        /// <param name="width">The width in pixels.</param>
        /// <param name="height">The height in pixels.</param>
        /// <param name="paramName">The parameter the dimensions come from.</param>
        /// <exception cref="ArgumentException">A dimension is not positive.</exception>
        static void ThrowIfInvalidSize(int width, int height, string paramName)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException($"The image size {width}x{height} is not positive.", paramName);
            }
        }

        /// <summary>
        /// Throws if the native storage has been released.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The data has been disposed.</exception>
        void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TextureData));
            }
        }
    }
}
