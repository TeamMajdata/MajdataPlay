using System;
using MajdataPlay.FFmpeg.Internal;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies that GPU NV12 conversion of camera captures yields exactly the samples of the CPU conversion.</summary>
    public unsafe class CaptureGpuConverterPlayModeTests
    {
        /// <summary>Tracks the textures created by the current test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Skips the fixture without a graphics device and creates the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("GPU conversion needs a graphics device.");
            }

            _objects = new TestObjects();
        }

        /// <summary>Destroys the created textures.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects?.Dispose();
        }

        /// <summary>Creates a capture-format render texture holding seeded random pixels.</summary>
        /// <param name="width">The texture width.</param>
        /// <param name="height">The texture height.</param>
        /// <param name="seed">The random seed.</param>
        /// <returns>The filled render texture.</returns>
        private RenderTexture CreateSource(int width, int height, int seed)
        {
            var pixels = new byte[width * height * 4];
            new System.Random(seed).NextBytes(pixels);
            var texture = _objects.Track(new Texture2D(width, height, TextureFormat.RGBA32, false, false));
            texture.filterMode = FilterMode.Point;
            texture.SetPixelData(pixels, 0);
            texture.Apply(false);
            var source = _objects.Track(CaptureGpuConverter.CreateRgbaTarget(width, height)!);
            // Point sampling makes both same-size copies exact.
            source.filterMode = FilterMode.Point;
            Graphics.Blit(texture, source);
            return source;
        }

        /// <summary>Reads a texture back synchronously.</summary>
        /// <param name="texture">The texture to read.</param>
        /// <param name="format">The readback format.</param>
        /// <returns>The bytes in readback row order.</returns>
        private static byte[] Read(Texture texture, TextureFormat format)
        {
            var request = AsyncGPUReadback.Request(texture, 0, format);
            request.WaitForCompletion();
            Assert.That(request.hasError, Is.False, "readback of " + texture.name);
            return request.GetData<byte>().ToArray();
        }

        /// <summary>Converts RGBA readback bytes to packed NV12 with the CPU kernel of the encoder worker.</summary>
        /// <param name="rgba">The RGBA readback in readback row order.</param>
        /// <param name="width">The frame width.</param>
        /// <param name="height">The frame height.</param>
        /// <param name="flipVertically">Whether the readback rows are bottom-first.</param>
        /// <returns>The Y plane followed by the interleaved Cb, Cr rows, top row first.</returns>
        private static byte[] ConvertOnCpu(byte[] rgba, int width, int height, bool flipVertically)
        {
            var nv12 = new byte[width * height * 3 / 2];
            fixed (byte* input = rgba)
            fixed (byte* output = nv12)
            {
                var planes = new Yuv420Planes
                {
                    Luma = output,
                    LumaStride = width,
                    BlueDifference = output + (width * height),
                    BlueDifferenceStride = width,
                    RedDifference = output + (width * height) + 1,
                    RedDifferenceStride = width,
                    ChromaStep = 2
                };
                var rowBytes = width * 4L;
                var top = flipVertically ? input + ((height - 1) * rowBytes) : input;
                VideoPixelKernels.ConvertRgbaToYuv420(top, flipVertically ? -rowBytes : rowBytes, width, in planes, 0, height / 2);
            }

            return nv12;
        }

        /// <summary>Checks GPU against CPU conversion in both row orders, at a size whose rows and row pairs are not multiples of four.</summary>
        /// <param name="flipVertically">Whether the capture rows are bottom-first.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void Record_MatchesCpuConversion(bool flipVertically)
        {
            const int width = 66;
            const int height = 38;
            var converter = CaptureGpuConverter.TryCreate(width, height, flipVertically, out var reason);
            if (converter == null)
            {
                Assert.Ignore("GPU conversion is unavailable: " + reason);
                return;
            }

            using (converter)
            {
                var source = CreateSource(width, height, flipVertically ? 2 : 1);
                var target = _objects.Track(converter.CreateTarget());
                using (var commands = new CommandBuffer())
                {
                    converter.Record(commands, source, Vector2.one, Vector2.zero, target);
                    Graphics.ExecuteCommandBuffer(commands);
                }

                var expected = ConvertOnCpu(Read(source, TextureFormat.RGBA32), width, height, flipVertically);
                var actual = Read(target, CaptureGpuConverter.ReadbackFormat);

                Assert.That(actual.Length, Is.EqualTo(expected.Length));
                for (var i = 0; i < expected.Length; i++)
                {
                    if (actual[i] != expected[i])
                    {
                        var plane = i < width * height ? "Y" : "CbCr";
                        var offset = i < width * height ? i : i - (width * height);
                        Assert.Fail(plane + " sample at column " + (offset % width) + ", row " + (offset / width) + " is "
                            + actual[i] + " instead of " + expected[i] + ".");
                    }
                }
            }
        }

        /// <summary>Checks argument validation and the NV12 target shape.</summary>
        [Test]
        public void TryCreate_ValidatesSizeAndCreatesNv12Targets()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CaptureGpuConverter.TryCreate(3, 2, false, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => CaptureGpuConverter.TryCreate(2, 5, false, out _));
            var converter = CaptureGpuConverter.TryCreate(64, 48, false, out var reason);
            if (converter == null)
            {
                Assert.That(reason, Is.Not.Null.And.Not.Empty);
                Assert.Ignore("GPU conversion is unavailable: " + reason);
                return;
            }

            using (converter)
            {
                Assert.That(reason, Is.Null);
                var target = _objects.Track(converter.CreateTarget());
                Assert.That(target.width, Is.EqualTo(64));
                Assert.That(target.height, Is.EqualTo(72));
                Assert.That(target.graphicsFormat, Is.EqualTo(UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm));
            }
        }
    }
}
