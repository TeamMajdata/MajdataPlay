using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using MajdataPlay.Drawing;
using NUnit.Framework;
using SkiaSharp;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Drawing
{
    /// <summary>
    /// Covers the conversions from Skia bitmaps, surfaces and encoded bytes to Unity textures:
    /// <see cref="SKBitmapExtensions"/>, <see cref="SKSurfaceExtensions"/> and <see cref="TextureLoader"/>.
    /// </summary>
    public class TextureConversionTests
    {
        /// <summary>Tracks the textures created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Creates the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
        }

        /// <summary>Destroys the textures created by the test.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Asserts that the base level of a texture holds the test pattern in Unity's bottom-up row order.</summary>
        /// <param name="texture">The readable texture.</param>
        internal static void AssertBaseLevel(Texture2D texture)
        {
            var pixels = texture.GetPixels32(0);
            for (var y = 0; y < texture.height; y++)
            {
                for (var x = 0; x < texture.width; x++)
                {
                    Assert.That(pixels[(y * texture.width) + x], Is.EqualTo(TestImages.UnityPixel(x, y, texture.height)), $"Pixel ({x}, {y})");
                }
            }
        }

        /// <summary>Asserts that every mipmap level is the box filter of the level above it.</summary>
        /// <param name="texture">The readable texture.</param>
        internal static void AssertMipChain(Texture2D texture)
        {
            var width = texture.width;
            var height = texture.height;
            var previous = texture.GetPixels32(0);
            for (var mip = 1; mip < texture.mipmapCount; mip++)
            {
                var mipWidth = Math.Max(1, width / 2);
                var mipHeight = Math.Max(1, height / 2);
                var level = texture.GetPixels32(mip);
                Assert.That(level.Length, Is.EqualTo(mipWidth * mipHeight), $"Mip {mip} size");
                for (var y = 0; y < mipHeight; y++)
                {
                    for (var x = 0; x < mipWidth; x++)
                    {
                        var expected = TestImages.BoxFilter(previous, width, height, x, y);
                        Assert.That(level[(y * mipWidth) + x], Is.EqualTo(expected), $"Mip {mip} pixel ({x}, {y})");
                    }
                }
                previous = level;
                width = mipWidth;
                height = mipHeight;
            }
        }

        /// <summary>Checks that an RGBA bitmap becomes a flipped, mipmapped, clamped and trilinear texture.</summary>
        [Test]
        public void BitmapToTexture2D_Rgba_FlipsRowsAndBuildsMips()
        {
            using var bitmap = TestImages.CreateBitmap(5, 3);

            var texture = _objects.Track(bitmap.ToTexture2D());

            Assert.That(texture.width, Is.EqualTo(5));
            Assert.That(texture.height, Is.EqualTo(3));
            Assert.That(texture.format, Is.EqualTo(TextureFormat.RGBA32));
            Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Trilinear));
            Assert.That(texture.mipmapCount, Is.EqualTo(3));
            Assert.That(texture.isReadable, Is.True);
            AssertBaseLevel(texture);
            AssertMipChain(texture);
        }

        /// <summary>Checks that a BGRA bitmap is converted to the RGBA channel order.</summary>
        [Test]
        public void BitmapToTexture2D_Bgra_ConvertsChannels()
        {
            using var bitmap = TestImages.CreateBitmap(4, 4, SKColorType.Bgra8888);

            var texture = _objects.Track(bitmap.ToTexture2D());

            AssertBaseLevel(texture);
            AssertMipChain(texture);
        }

        /// <summary>Checks that requesting a non-readable texture releases the CPU copy.</summary>
        [Test]
        public void BitmapToTexture2D_MarkNonReadable_ReleasesCpuCopy()
        {
            using var bitmap = TestImages.CreateBitmap(2, 2);

            var texture = _objects.Track(bitmap.ToTexture2D(true));

            Assert.That(texture.isReadable, Is.False);
        }

        /// <summary>Checks that a single-pixel bitmap produces a single-level texture.</summary>
        [Test]
        public void BitmapToTexture2D_SinglePixel_HasOneLevel()
        {
            using var bitmap = TestImages.CreateBitmap(1, 1);

            var texture = _objects.Track(bitmap.ToTexture2D());

            Assert.That(texture.mipmapCount, Is.EqualTo(1));
            Assert.That(texture.GetPixel(0, 0), Is.EqualTo((Color)TestImages.UnityPixel(0, 0, 1)));
        }

        /// <summary>Checks that a surface's rendered pixels are read into a texture.</summary>
        [Test]
        public void SurfaceToTexture2D_ReadsRenderedPixels()
        {
            var info = new SKImageInfo(4, 2, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            surface.Canvas.Clear(new SKColor(200, 100, 50, 255));

            var texture = _objects.Track(surface.ToTexture2D(info));

            Assert.That(texture.width, Is.EqualTo(4));
            Assert.That(texture.height, Is.EqualTo(2));
            foreach (var pixel in texture.GetPixels32())
            {
                Assert.That(pixel, Is.EqualTo(new Color32(200, 100, 50, 255)));
            }
        }

        /// <summary>Checks that encoded PNG bytes load into a texture that honours the readability flag.</summary>
        [Test]
        public void TextureLoader_LoadFromMemory_DecodesPng()
        {
            var png = TestImages.CreatePng(6, 4);

            var readable = _objects.Track(TextureLoader.LoadFromMemory(png, false));
            var nonReadable = _objects.Track(TextureLoader.LoadFromMemory(png, true));

            Assert.That(readable.width, Is.EqualTo(6));
            Assert.That(readable.height, Is.EqualTo(4));
            AssertBaseLevel(readable);
            Assert.That(nonReadable.isReadable, Is.False);
        }

        /// <summary>Checks that undecodable bytes cannot be loaded.</summary>
        [Test]
        public void TextureLoader_LoadFromMemory_InvalidData_Throws()
        {
            Assert.Catch<Exception>(() => TextureLoader.LoadFromMemory(new byte[] { 1, 2, 3, 4 }, false));
        }

        /// <summary>Checks that the asynchronous loader decodes off the main thread and returns a texture on it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TextureLoader_LoadFromMemoryAsync_DecodesPng() => UniTask.ToCoroutine(async () =>
        {
            var png = TestImages.CreatePng(3, 2);

            var texture = _objects.Track(await TextureLoader.LoadFromMemoryAsync(png, false));

            Assert.That(texture.width, Is.EqualTo(3));
            Assert.That(texture.height, Is.EqualTo(2));
            AssertBaseLevel(texture);
        });

        /// <summary>Checks that the mipmap overload of the synchronous loader builds or skips the chain.</summary>
        [Test]
        public void TextureLoader_LoadFromMemory_MipmapOverload()
        {
            var png = TestImages.CreatePng(6, 4);

            var withMips = _objects.Track(TextureLoader.LoadFromMemory(png, false, true));
            var withoutMips = _objects.Track(TextureLoader.LoadFromMemory(png, false, false));

            Assert.That(withMips.mipmapCount, Is.EqualTo(3));
            Assert.That(withMips.filterMode, Is.EqualTo(FilterMode.Trilinear));
            AssertMipChain(withMips);
            Assert.That(withoutMips.mipmapCount, Is.EqualTo(1));
            Assert.That(withoutMips.filterMode, Is.EqualTo(FilterMode.Bilinear));
            AssertBaseLevel(withoutMips);
        }

        /// <summary>Checks that the mipmap overload of the asynchronous loader builds or skips the chain and returns on the main thread.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TextureLoader_LoadFromMemoryAsync_MipmapOverload() => UniTask.ToCoroutine(async () =>
        {
            var png = TestImages.CreatePng(6, 4);

            var withMips = _objects.Track(await TextureLoader.LoadFromMemoryAsync(png, false, true));
            var withoutMips = _objects.Track(await TextureLoader.LoadFromMemoryAsync(png, false, false));

            Assert.That(PlayerLoopHelper.IsMainThread, Is.True);
            Assert.That(withMips.mipmapCount, Is.EqualTo(3));
            AssertBaseLevel(withMips);
            AssertMipChain(withMips);
            Assert.That(withoutMips.mipmapCount, Is.EqualTo(1));
            AssertBaseLevel(withoutMips);
        });

        /// <summary>Checks that the asynchronous loader, started on a worker thread, decodes there and still uploads on the main thread.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TextureLoader_LoadFromMemoryAsync_FromWorkerThread() => UniTask.ToCoroutine(async () =>
        {
            var png = TestImages.CreatePng(5, 3);

            var texture = _objects.Track(await System.Threading.Tasks.Task.Run(() => TextureLoader.LoadFromMemoryAsync(png, false, true)));

            Assert.That(texture.mipmapCount, Is.EqualTo(3));
            AssertBaseLevel(texture);
        });

        /// <summary>Checks that the asynchronous loader reports invalid data and cancellation.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TextureLoader_LoadFromMemoryAsync_InvalidDataOrCanceled_Throws() => UniTask.ToCoroutine(async () =>
        {
            var png = TestImages.CreatePng(2, 2);
            Exception? invalid = null;
            Exception? canceled = null;

            try
            {
                await TextureLoader.LoadFromMemoryAsync(new byte[] { 1, 2, 3, 4 }, false, true);
            }
            catch (Exception e)
            {
                invalid = e;
            }
            try
            {
                await TextureLoader.LoadFromMemoryAsync(png, false, true, new System.Threading.CancellationToken(true));
            }
            catch (Exception e)
            {
                canceled = e;
            }

            Assert.That(invalid, Is.InstanceOf<ArgumentException>());
            Assert.That(canceled, Is.InstanceOf<OperationCanceledException>());
        });

        /// <summary>Checks that the mipmap overloads of the bitmap and surface conversions skip the chain on request.</summary>
        [Test]
        public void ToTexture2D_WithoutMipmaps_HasOneLevel()
        {
            using var bitmap = TestImages.CreateBitmap(4, 4, SKColorType.Bgra8888);
            var info = new SKImageInfo(4, 2, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            surface.Canvas.Clear(new SKColor(200, 100, 50, 255));

            var fromBitmap = _objects.Track(bitmap.ToTexture2D(false, false));
            var fromSurface = _objects.Track(surface.ToTexture2D(info, false));

            Assert.That(fromBitmap.mipmapCount, Is.EqualTo(1));
            AssertBaseLevel(fromBitmap);
            Assert.That(fromSurface.mipmapCount, Is.EqualTo(1));
            Assert.That(fromSurface.GetPixel(3, 1), Is.EqualTo((Color)new Color32(200, 100, 50, 255)));
        }
    }
}
