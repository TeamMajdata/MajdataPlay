using System;
using System.IO;
using MajdataPlay.Drawing;
using MajdataPlay.IO.Storage;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Drawing
{
    /// <summary>
    /// Covers <see cref="EncodedImage"/> and <see cref="DecodedImage"/>: header reading, decoding into bottom-up RGBA32
    /// pixels, texture creation, and disposal of the native storage.
    /// </summary>
    public class ImageDecodingTests
    {
        /// <summary>Holds the test files.</summary>
        private TemporaryDirectory _directory = null!;

        /// <summary>Tracks the textures created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Creates the temporary directory and the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _directory = new TemporaryDirectory();
            _objects = new TestObjects();
        }

        /// <summary>Destroys the created objects and the temporary directory.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            _directory.Dispose();
        }

        /// <summary>Writes bytes to a temporary file.</summary>
        /// <param name="name">The file name.</param>
        /// <param name="bytes">The content.</param>
        /// <returns>The file.</returns>
        private StorageFile WriteFile(string name, byte[] bytes)
        {
            var file = _directory.Directory.CreateFile(name);
            file.WriteAllBytes(bytes);
            return file;
        }

        /// <summary>Opens and decodes a PNG of the test pattern.</summary>
        /// <param name="width">The image width.</param>
        /// <param name="height">The image height.</param>
        /// <returns>The decoded image.</returns>
        private DecodedImage Decode(int width, int height)
        {
            using var encoded = EncodedImage.Open(WriteFile("image.png", TestImages.CreatePng(width, height)));
            return encoded.Decode() ?? throw new AssertionException("The PNG could not be decoded.");
        }

        /// <summary>Asserts that the pixels hold the test pattern in bottom-up row order.</summary>
        /// <param name="image">The decoded image.</param>
        private static void AssertPattern(DecodedImage image)
        {
            var pixels = image.Pixels;
            Assert.That(pixels.Length, Is.EqualTo(image.Width * image.Height));
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    Assert.That(pixels[(y * image.Width) + x], Is.EqualTo(TestImages.UnityPixel(x, y, image.Height)), $"Pixel ({x}, {y})");
                }
            }
        }

        /// <summary>Checks that a null file is rejected.</summary>
        [Test]
        public void Open_NullFile_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => EncodedImage.Open(null!));
        }

        /// <summary>Checks that a missing file is reported.</summary>
        [Test]
        public void Open_MissingFile_Throws()
        {
            var file = FileSystem.File.Open(_directory.Location + "/missing.png");

            Assert.Throws<FileNotFoundException>(() => EncodedImage.Open(file));
        }

        /// <summary>Checks that a PNG's header dimensions are read without decoding.</summary>
        [Test]
        public void Open_Png_ReadsHeader()
        {
            using var encoded = EncodedImage.Open(WriteFile("header.png", TestImages.CreatePng(6, 3)));

            Assert.That(encoded.Width, Is.EqualTo(6));
            Assert.That(encoded.Height, Is.EqualTo(3));
            Assert.That(encoded.IsSupported, Is.True);
        }

        /// <summary>Checks that data in an unknown format is unsupported and decodes to nothing.</summary>
        [Test]
        public void Open_UnknownFormat_IsUnsupported()
        {
            using var encoded = EncodedImage.Open(_directory.WriteText("text.png", "plain text"));

            Assert.That(encoded.Width, Is.EqualTo(0));
            Assert.That(encoded.Height, Is.EqualTo(0));
            Assert.That(encoded.IsSupported, Is.False);
            Assert.That(encoded.Decode(), Is.Null);
        }

        /// <summary>Checks that a disposed encoded image refuses to decode and can be disposed again.</summary>
        [Test]
        public void EncodedImage_Disposed_RejectsDecode()
        {
            var encoded = EncodedImage.Open(WriteFile("disposed.png", TestImages.CreatePng(2, 2)));

            encoded.Dispose();
            encoded.Dispose();

            Assert.Throws<ObjectDisposedException>(() => encoded.Decode());
        }

        /// <summary>Checks that an image with an even row count is decoded into bottom-up rows.</summary>
        [Test]
        public void Decode_EvenHeight_FlipsRows()
        {
            using var image = Decode(5, 4);

            Assert.That(image.Width, Is.EqualTo(5));
            Assert.That(image.Height, Is.EqualTo(4));
            AssertPattern(image);
        }

        /// <summary>Checks that an image with an odd row count keeps its middle row while flipping the others.</summary>
        [Test]
        public void Decode_OddHeight_FlipsRows()
        {
            using var image = Decode(3, 3);

            AssertPattern(image);
        }

        /// <summary>Checks that a single-row image decodes unchanged.</summary>
        [Test]
        public void Decode_SingleRow_KeepsRow()
        {
            using var image = Decode(4, 1);

            AssertPattern(image);
        }

        /// <summary>Checks that a truncated file still decodes the rows that are present.</summary>
        [Test]
        public void Decode_TruncatedData_ReturnsPartialImage()
        {
            var png = TestImages.CreateNoisePng(64);
            var truncated = new byte[png.Length * 2 / 3];
            Array.Copy(png, truncated, truncated.Length);
            using var encoded = EncodedImage.Open(WriteFile("truncated.png", truncated));

            Assert.That(encoded.IsSupported, Is.True);
            using var image = encoded.Decode();

            Assert.That(image, Is.Not.Null);
            Assert.That(image!.Width, Is.EqualTo(64));
            Assert.That(image.Height, Is.EqualTo(64));
        }

        /// <summary>Checks that the pixel view is created once and reused.</summary>
        [Test]
        public unsafe void Pixels_ReturnsSameView()
        {
            using var image = Decode(2, 2);

            var first = image.Pixels;
            var second = image.Pixels;

            Assert.That((IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(first), Is.EqualTo((IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(second)));
        }

        /// <summary>Checks that ToTexture2D uploads the pixels with a full mip chain and the requested readability.</summary>
        [Test]
        public void ToTexture2D_UploadsPixels()
        {
            using var image = Decode(4, 2);

            var readable = _objects.Track(image.ToTexture2D(false));
            var nonReadable = _objects.Track(image.ToTexture2D(true));

            Assert.That(readable.width, Is.EqualTo(4));
            Assert.That(readable.height, Is.EqualTo(2));
            Assert.That(readable.format, Is.EqualTo(TextureFormat.RGBA32));
            Assert.That(readable.mipmapCount, Is.EqualTo(3));
            Assert.That(readable.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(readable.filterMode, Is.EqualTo(FilterMode.Trilinear));
            Assert.That(readable.GetPixels32(0), Is.EqualTo(image.Pixels.ToArray()));
            Assert.That(nonReadable.isReadable, Is.False);
        }

        /// <summary>Checks that a disposed image rejects access and invalidates a previously obtained pixel view.</summary>
        [Test]
        public void Dispose_InvalidatesImage()
        {
            var image = Decode(2, 2);
            var pixels = image.Pixels;

            image.Dispose();
            image.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _ = image.Pixels);
            Assert.Throws<ObjectDisposedException>(() => image.ToTexture2D(false));
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Assert.That(() => _ = pixels[0], Throws.InstanceOf<InvalidOperationException>());
#endif
        }

        /// <summary>Checks that an image whose pixels were never viewed can be disposed.</summary>
        [Test]
        public void Dispose_WithoutPixelView_Succeeds()
        {
            var image = Decode(2, 2);

            Assert.DoesNotThrow(() => image.Dispose());
            Assert.Throws<ObjectDisposedException>(() => _ = image.Pixels);
        }
    }
}
