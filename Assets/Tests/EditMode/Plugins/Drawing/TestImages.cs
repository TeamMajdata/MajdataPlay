using System;
using SkiaSharp;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Drawing
{
    /// <summary>Creates small, deterministic test images with SkiaSharp.</summary>
    public static class TestImages
    {
        /// <summary>Gets the opaque colour of a pixel in Skia's top-down coordinates.</summary>
        /// <param name="x">The column.</param>
        /// <param name="y">The row, counted from the top.</param>
        /// <returns>A colour unique to the pixel for images up to 6 by 4 pixels.</returns>
        public static SKColor PixelColor(int x, int y)
        {
            return new SKColor((byte)(x * 40), (byte)(y * 60), (byte)(10 + (x * 7) + y), 255);
        }

        /// <summary>Gets the Unity colour of a pixel in Unity's bottom-up coordinates.</summary>
        /// <param name="x">The column.</param>
        /// <param name="unityRow">The row, counted from the bottom.</param>
        /// <param name="height">The image height.</param>
        /// <returns>The colour Skia stores at the mirrored row.</returns>
        public static Color32 UnityPixel(int x, int unityRow, int height)
        {
            var color = PixelColor(x, height - 1 - unityRow);
            return new Color32(color.Red, color.Green, color.Blue, color.Alpha);
        }

        /// <summary>Creates a bitmap filled with <see cref="PixelColor"/>.</summary>
        /// <param name="width">The width in pixels.</param>
        /// <param name="height">The height in pixels.</param>
        /// <param name="colorType">The pixel format.</param>
        /// <returns>The bitmap, owned by the caller.</returns>
        public static SKBitmap CreateBitmap(int width, int height, SKColorType colorType = SKColorType.Rgba8888)
        {
            var bitmap = new SKBitmap(new SKImageInfo(width, height, colorType, SKAlphaType.Unpremul));
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    bitmap.SetPixel(x, y, PixelColor(x, y));
                }
            }
            return bitmap;
        }

        /// <summary>Encodes a bitmap filled with <see cref="PixelColor"/> as PNG.</summary>
        /// <param name="width">The width in pixels.</param>
        /// <param name="height">The height in pixels.</param>
        /// <returns>The PNG bytes.</returns>
        public static byte[] CreatePng(int width, int height)
        {
            using var bitmap = CreateBitmap(width, height);
            return Encode(bitmap);
        }

        /// <summary>Encodes a large bitmap of pseudo-random noise as PNG, so that its pixel data spans many bytes.</summary>
        /// <param name="size">The width and height in pixels.</param>
        /// <returns>The PNG bytes.</returns>
        public static byte[] CreateNoisePng(int size)
        {
            var random = new System.Random(1234);
            using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    bitmap.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255));
                }
            }
            return Encode(bitmap);
        }

        /// <summary>Encodes a bitmap as PNG.</summary>
        /// <param name="bitmap">The bitmap.</param>
        /// <returns>The PNG bytes.</returns>
        /// <exception cref="InvalidOperationException">Skia could not encode the bitmap.</exception>
        public static byte[] Encode(SKBitmap bitmap)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidOperationException("PNG encoding failed.");
            return data.ToArray();
        }

        /// <summary>Computes the box-filtered pixel the mipmap generator produces from a 2x2 block.</summary>
        /// <param name="source">The source level, in Unity row order.</param>
        /// <param name="sourceWidth">The source width.</param>
        /// <param name="sourceHeight">The source height.</param>
        /// <param name="x">The destination column.</param>
        /// <param name="y">The destination row.</param>
        /// <returns>The averaged colour.</returns>
        public static Color32 BoxFilter(Color32[] source, int sourceWidth, int sourceHeight, int x, int y)
        {
            var sx = x * 2;
            var sy = y * 2;
            var sx1 = Math.Min(sx + 1, sourceWidth - 1);
            var sy1 = Math.Min(sy + 1, sourceHeight - 1);
            var c00 = source[(sy * sourceWidth) + sx];
            var c10 = source[(sy * sourceWidth) + sx1];
            var c01 = source[(sy1 * sourceWidth) + sx];
            var c11 = source[(sy1 * sourceWidth) + sx1];
            return new Color32(
                (byte)((c00.r + c10.r + c01.r + c11.r) >> 2),
                (byte)((c00.g + c10.g + c01.g + c11.g) >> 2),
                (byte)((c00.b + c10.b + c01.b + c11.b) >> 2),
                (byte)((c00.a + c10.a + c01.a + c11.a) >> 2));
        }
    }
}
