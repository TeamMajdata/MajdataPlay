using System;
using MajdataPlay.Drawing;
using NUnit.Framework;
using SkiaSharp;
using Unity.Collections;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Drawing
{
    /// <summary>
    /// Covers the colour conversions between Skia and Unity: <see cref="Color32Extensions"/>,
    /// <see cref="SKColorExtensions"/> and the batched job-based <see cref="SKColorConverter"/>.
    /// </summary>
    public class ColorConversionTests
    {
        /// <summary>Checks that a Color32 converts to the SKColor with the same channels.</summary>
        [Test]
        public void Color32ToSkColor_KeepsChannels()
        {
            var color = new Color32(10, 20, 30, 40).ToSkColor();

            Assert.That(color, Is.EqualTo(new SKColor(10, 20, 30, 40)));
        }

        /// <summary>Checks that a floating point Color is quantized to bytes when converted to SKColor.</summary>
        [Test]
        public void ColorToSkColor_QuantizesChannels()
        {
            var color = new Color(1f, 0f, 0.2f, 1f).ToSkColor();

            Assert.That(color, Is.EqualTo(new SKColor(255, 0, 51, 255)));
        }

        /// <summary>Checks that a Color converts to the SKColorF with the same channels.</summary>
        [Test]
        public void ColorToSkColorF_KeepsChannels()
        {
            var color = new Color(0.1f, 0.2f, 0.3f, 0.4f).ToSkColorF();

            Assert.That(color.Red, Is.EqualTo(0.1f));
            Assert.That(color.Green, Is.EqualTo(0.2f));
            Assert.That(color.Blue, Is.EqualTo(0.3f));
            Assert.That(color.Alpha, Is.EqualTo(0.4f));
        }

        /// <summary>Checks that an SKColor converts to the Color32 with the same channels.</summary>
        [Test]
        public void SkColorToUnityColor32_KeepsChannels()
        {
            Assert.That(new SKColor(1, 2, 3, 4).ToUnityColor32(), Is.EqualTo(new Color32(1, 2, 3, 4)));
        }

        /// <summary>Checks that an SKColorF converts to the Color with the same channels.</summary>
        [Test]
        public void SkColorFToUnityColor_KeepsChannels()
        {
            Assert.That(new SKColorF(0.5f, 0.25f, 0.125f, 1f).ToUnityColor(), Is.EqualTo(new Color(0.5f, 0.25f, 0.125f, 1f)));
        }

        /// <summary>Checks that spans and memory of SKColors convert element-wise, including empty input.</summary>
        [Test]
        public void SkColorBuffersToUnityColor32_ConvertEveryElement()
        {
            var colors = new SKColor[37];
            for (var i = 0; i < colors.Length; i++)
            {
                colors[i] = new SKColor((byte)i, (byte)(i * 2), (byte)(i * 3), (byte)(255 - i));
            }

            var fromSpan = SKColorExtensions.ToUnityColor32(new ReadOnlySpan<SKColor>(colors));
            var fromMemory = SKColorExtensions.ToUnityColor32(new ReadOnlyMemory<SKColor>(colors));
            var empty = SKColorExtensions.ToUnityColor32(ReadOnlySpan<SKColor>.Empty);

            Assert.That(fromSpan.Length, Is.EqualTo(colors.Length));
            for (var i = 0; i < colors.Length; i++)
            {
                var expected = new Color32((byte)i, (byte)(i * 2), (byte)(i * 3), (byte)(255 - i));
                Assert.That(fromSpan[i], Is.EqualTo(expected));
                Assert.That(fromMemory[i], Is.EqualTo(expected));
            }
            Assert.That(empty, Is.Empty);
        }

        /// <summary>Checks that the batched conversion to Color32 swaps the red and blue bytes of the packed colours.</summary>
        [Test]
        public void ConvertToColor32_ConvertsBatch()
        {
            var colors = new NativeArray<SKColor>(3, Allocator.TempJob);
            colors[0] = new SKColor(10, 20, 30, 40);
            colors[1] = new SKColor(255, 0, 0, 255);
            colors[2] = new SKColor(0, 0, 255, 128);
            var converted = SKColorConverter.ConvertToColor32(colors, 1);
            try
            {
                Assert.That(converted.Length, Is.EqualTo(3));
                Assert.That(converted[0], Is.EqualTo(new Color32(10, 20, 30, 40)));
                Assert.That(converted[1], Is.EqualTo(new Color32(255, 0, 0, 255)));
                Assert.That(converted[2], Is.EqualTo(new Color32(0, 0, 255, 128)));
            }
            finally
            {
                converted.Dispose();
                colors.Dispose();
            }
        }

        /// <summary>Checks that the batched conversion to SKColor is the inverse of the conversion to Color32.</summary>
        [Test]
        public void ConvertToSkColor_ConvertsBatch()
        {
            var colors = new NativeArray<Color32>(2, Allocator.TempJob);
            colors[0] = new Color32(1, 2, 3, 4);
            colors[1] = new Color32(200, 100, 50, 255);
            var converted = SKColorConverter.ConvertToSkColor(colors);
            try
            {
                Assert.That(converted[0], Is.EqualTo(new SKColor(1, 2, 3, 4)));
                Assert.That(converted[1], Is.EqualTo(new SKColor(200, 100, 50, 255)));
            }
            finally
            {
                converted.Dispose();
                colors.Dispose();
            }
        }

        /// <summary>Checks that the low-level converter swaps bytes 0 and 2 of every packed value and leaves the others.</summary>
        [Test]
        public void FastColorConverter_SwapsRedAndBlueBytes()
        {
            var input = new NativeArray<uint>(new[] { 0xAABBCCDDu, 0x00000000u, 0x11223344u }, Allocator.TempJob);
            var handle = SKColorConverter.FastColorConverter(input, out var output, 2);
            try
            {
                handle.Complete();

                Assert.That(output.ToArray(), Is.EqualTo(new[] { 0xAADDCCBBu, 0x00000000u, 0x11443322u }));
            }
            finally
            {
                handle.Complete();
                output.Dispose();
                input.Dispose();
            }
        }
    }
}
