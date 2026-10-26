using SkiaSharp;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Drawing
{
    /// <summary>
    /// Converts Skia bitmaps to Unity textures through <see cref="TextureData"/>.
    /// </summary>
    public static class SKBitmapExtensions
    {
        /// <summary>
        /// Creates a clamped, trilinear RGBA32 texture with a full box-filtered mipmap chain. Call on the main thread;
        /// to keep conversion off it, build a <see cref="TextureData"/> on a worker thread instead.
        /// </summary>
        /// <param name="bitmap">The source bitmap in Skia's top-down row order.</param>
        /// <param name="markNonReadable">Whether to release the texture's CPU copy after upload.</param>
        /// <returns>The uploaded texture.</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="bitmap"/> is null.</exception>
        /// <exception cref="System.ArgumentException">The bitmap is empty, has no pixels or cannot be converted.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">The image does not fit in one buffer.</exception>
        public static Texture2D ToTexture2D(this SKBitmap bitmap, bool markNonReadable = false)
        {
            return ToTexture2D(bitmap, markNonReadable, true);
        }

        /// <summary>
        /// Creates a clamped RGBA32 texture, trilinear with a full box-filtered mipmap chain or bilinear without one.
        /// Call on the main thread; to keep conversion off it, build a <see cref="TextureData"/> on a worker thread instead.
        /// </summary>
        /// <param name="bitmap">The source bitmap in Skia's top-down row order.</param>
        /// <param name="markNonReadable">Whether to release the texture's CPU copy after upload.</param>
        /// <param name="generateMipmaps">Whether to build a mipmap chain.</param>
        /// <returns>The uploaded texture.</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="bitmap"/> is null.</exception>
        /// <exception cref="System.ArgumentException">The bitmap is empty, has no pixels or cannot be converted.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">The image does not fit in one buffer.</exception>
        public static Texture2D ToTexture2D(this SKBitmap bitmap, bool markNonReadable, bool generateMipmaps)
        {
            using var data = TextureData.FromBitmap(bitmap, generateMipmaps);

            return data.ToTexture2D(markNonReadable);
        }
    }
}
