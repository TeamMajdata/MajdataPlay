using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Drawing
{
    /// <summary>
    /// Converts Skia surfaces to Unity textures.
    /// </summary>
    public static class SKSurfaceExtensions
    {
        /// <summary>
        /// Reads a surface into a clamped, trilinear RGBA32 texture with a full mipmap chain. Call on the main thread.
        /// </summary>
        /// <param name="surface">The surface to read.</param>
        /// <param name="imageInfo">The surface's image info; only its size is used.</param>
        /// <returns>The uploaded, readable texture.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="surface"/> is null.</exception>
        /// <exception cref="ArgumentException">The size is not positive.</exception>
        /// <exception cref="InvalidOperationException">The surface pixels could not be read.</exception>
        public static Texture2D ToTexture2D(this SKSurface surface, SKImageInfo imageInfo)
        {
            return ToTexture2D(surface, imageInfo, true);
        }

        /// <summary>
        /// Reads a surface into a clamped RGBA32 texture, trilinear with a full mipmap chain or bilinear without one.
        /// Call on the main thread.
        /// </summary>
        /// <param name="surface">The surface to read.</param>
        /// <param name="imageInfo">The surface's image info; only its size is used.</param>
        /// <param name="generateMipmaps">Whether to build a mipmap chain.</param>
        /// <returns>The uploaded, readable texture.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="surface"/> is null.</exception>
        /// <exception cref="ArgumentException">The size is not positive.</exception>
        /// <exception cref="InvalidOperationException">The surface pixels could not be read.</exception>
        public static Texture2D ToTexture2D(this SKSurface surface, SKImageInfo imageInfo, bool generateMipmaps)
        {
            if (surface is null)
            {
                throw new ArgumentNullException(nameof(surface));
            }
            // Let Skia convert straight to the texture layout while reading, so no pixmap conversion is needed later.
            var info = new SKImageInfo(imageInfo.Width, imageInfo.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var bitmap = new SKBitmap();
            if (info.Width <= 0 || info.Height <= 0 || !bitmap.TryAllocPixels(info))
            {
                throw new ArgumentException($"Cannot read a {info.Width}x{info.Height} surface.", nameof(imageInfo));
            }
            if (!surface.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
            {
                throw new InvalidOperationException("The surface pixels could not be read.");
            }
            using var data = TextureData.FromBitmap(bitmap, generateMipmaps);

            return data.ToTexture2D(false);
        }
    }
}
