using System;
using System.Collections.Generic;
using MajdataPlay.Scenes.Game.Notes.Skins;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Skins
{
    /// <summary>
    /// Checks the skyline packing of <see cref="AtlasLayout"/>, porting the expectations of the standalone atlas layout
    /// validation.
    /// </summary>
    public class AtlasLayoutTests
    {
        /// <summary>The padding used by the skin atlas builder.</summary>
        private const int Padding = 2;

        /// <summary>Checks argument validation for padding, maximum size, empty input and non-positive dimensions.</summary>
        [Test]
        public void Create_ValidatesArguments()
        {
            var valid = new List<(int Width, int Height)> { (4, 4) };

            Assert.Throws<ArgumentOutOfRangeException>(() => AtlasLayout.Create(valid, -1, 8192));
            Assert.Throws<ArgumentOutOfRangeException>(() => AtlasLayout.Create(valid, Padding, 0));
            Assert.Throws<ArgumentException>(() => AtlasLayout.Create(new List<(int Width, int Height)>(), Padding, 8192));
            Assert.Throws<ArgumentException>(() => AtlasLayout.Create(new List<(int Width, int Height)> { (0, 4) }, Padding, 8192));
            Assert.Throws<ArgumentException>(() => AtlasLayout.Create(new List<(int Width, int Height)> { (4, -1) }, Padding, 8192));
        }

        /// <summary>Checks that a single pixel uses the smallest aligned atlas.</summary>
        [Test]
        public void Create_SinglePixelUsesAFourByFourAtlas()
        {
            var layout = AtlasLayout.Create(new List<(int Width, int Height)> { (1, 1) }, Padding, 8192);

            Assert.That(layout.Width, Is.EqualTo(4));
            Assert.That(layout.Height, Is.EqualTo(4));
            Assert.That(layout.Scale, Is.EqualTo(1.0));
            Assert.That(layout.Count, Is.EqualTo(1));
            Assert.That((layout[0].X, layout[0].Y, layout[0].Width, layout[0].Height), Is.EqualTo((0, 0, 1, 1)));
        }

        /// <summary>Checks a small set: tallest first, slots in input order and the area-minimising width.</summary>
        [Test]
        public void Create_PacksTallestImagesFirstAndKeepsInputOrder()
        {
            var sizes = new List<(int Width, int Height)> { (10, 20), (30, 5), (7, 7) };

            var layout = AtlasLayout.Create(sizes, Padding, 64);

            Assert.That((layout.Width, layout.Height), Is.EqualTo((32, 28)));
            Assert.That((layout[0].X, layout[0].Y, layout[0].Width, layout[0].Height), Is.EqualTo((0, 0, 10, 20)));
            Assert.That((layout[1].X, layout[1].Y, layout[1].Width, layout[1].Height), Is.EqualTo((0, 22, 30, 5)));
            Assert.That((layout[2].X, layout[2].Y, layout[2].Width, layout[2].Height), Is.EqualTo((12, 0, 7, 7)));
            Validate(sizes, layout, 64);
        }

        /// <summary>Checks that rows of equal images stack without padding when the padding is zero.</summary>
        [Test]
        public void Create_StacksEqualRowsWithoutPadding()
        {
            var sizes = new List<(int Width, int Height)> { (300, 10), (300, 10), (300, 10) };

            var layout = AtlasLayout.Create(sizes, 0, 1024);

            Assert.That((layout.Width, layout.Height), Is.EqualTo((300, 32)));
            Assert.That(layout[1].Y, Is.EqualTo(10));
            Assert.That(layout[2].Y, Is.EqualTo(20));
        }

        /// <summary>Checks that an oversized image sets one uniform scale for every image.</summary>
        [Test]
        public void Create_ScalesOversizedImagesUniformly()
        {
            var sizes = new List<(int Width, int Height)> { (10000, 50), (200, 100) };

            var layout = AtlasLayout.Create(sizes, Padding, 8192);

            Assert.That(layout.Scale, Is.EqualTo(0.8192).Within(1e-9));
            Assert.That((layout[0].Width, layout[0].Height), Is.EqualTo((8192, 41)));
            Assert.That((layout[1].Width, layout[1].Height), Is.EqualTo((164, 82)));
            Validate(sizes, layout, 8192);
        }

        /// <summary>Checks that an oversubscribed set shrinks until it fits.</summary>
        [Test]
        public void Create_ShrinksAnOversubscribedSet()
        {
            var sizes = new List<(int Width, int Height)>();
            for (var i = 0; i < 100; i++)
            {
                sizes.Add((1000, 1000));
            }

            var layout = AtlasLayout.Create(sizes, Padding, 2048);

            Assert.That(layout.Scale, Is.LessThan(1.0));
            Validate(sizes, layout, 2048);
        }

        /// <summary>Checks that a set that cannot fit even after shrinking is rejected.</summary>
        [Test]
        public void Create_RejectsSetsThatNeverFit()
        {
            var sizes = new List<(int Width, int Height)> { (1, 1), (1, 1) };

            Assert.Throws<InvalidOperationException>(() => AtlasLayout.Create(sizes, 10, 4));
        }

        /// <summary>Checks random sets against every layout invariant for several limits.</summary>
        [Test]
        public void Create_RandomSetsSatisfyEveryInvariant()
        {
            var limits = new[] { 2048, 4096, 8192 };
            for (var seed = 1; seed <= 12; seed++)
            {
                var random = new Random(seed);
                var count = random.Next(1, 120);
                var maximumSide = random.Next(1, 1300);
                var sizes = new List<(int Width, int Height)>(count);
                for (var i = 0; i < count; i++)
                {
                    sizes.Add((random.Next(1, maximumSide + 1), random.Next(1, maximumSide + 1)));
                }
                var limit = limits[seed % limits.Length];
                Validate(sizes, AtlasLayout.Create(sizes, Padding, limit), limit);
            }
        }

        /// <summary>Checks that identical input produces identical layouts.</summary>
        [Test]
        public void Create_IsDeterministic()
        {
            var random = new Random(1234);
            var sizes = new List<(int Width, int Height)>();
            for (var i = 0; i < 80; i++)
            {
                sizes.Add((random.Next(1, 600), random.Next(1, 600)));
            }

            var first = AtlasLayout.Create(sizes, Padding, 4096);
            var second = AtlasLayout.Create(sizes, Padding, 4096);

            Assert.That((first.Width, first.Height), Is.EqualTo((second.Width, second.Height)));
            for (var i = 0; i < sizes.Count; i++)
            {
                Assert.That((first[i].X, first[i].Y), Is.EqualTo((second[i].X, second[i].Y)));
            }
        }

        /// <summary>Checks that an atlas slot keeps its constructor arguments.</summary>
        [Test]
        public void AtlasSlot_StoresItsRectangle()
        {
            var slot = new AtlasSlot(1, 2, 3, 4);

            Assert.That((slot.X, slot.Y, slot.Width, slot.Height), Is.EqualTo((1, 2, 3, 4)));
        }

        /// <summary>Checks every layout invariant: slot count, bounds, alignment, uniform scale and padding.</summary>
        /// <param name="sizes">The source sizes.</param>
        /// <param name="layout">The layout under test.</param>
        /// <param name="limit">The maximum atlas size.</param>
        private static void Validate(List<(int Width, int Height)> sizes, AtlasLayout layout, int limit)
        {
            Assert.That(layout.Count, Is.EqualTo(sizes.Count));
            Assert.That(layout.Width, Is.InRange(1, limit));
            Assert.That(layout.Height, Is.InRange(1, limit));
            Assert.That(layout.Width % 4, Is.EqualTo(0));
            Assert.That(layout.Height % 4, Is.EqualTo(0));
            Assert.That(layout.Scale, Is.GreaterThan(0).And.LessThanOrEqualTo(1));
            for (var i = 0; i < sizes.Count; i++)
            {
                var slot = layout[i];
                Assert.That(slot.Width, Is.EqualTo(Scaled(sizes[i].Width, layout.Scale, limit)), $"Width of slot {i}");
                Assert.That(slot.Height, Is.EqualTo(Scaled(sizes[i].Height, layout.Scale, limit)), $"Height of slot {i}");
                Assert.That(slot.X >= 0 && slot.Y >= 0 && slot.X + slot.Width <= layout.Width && slot.Y + slot.Height <= layout.Height, Is.True, $"Slot {i} inside the atlas");
                for (var j = i + 1; j < sizes.Count; j++)
                {
                    var other = layout[j];
                    var separated = slot.X + slot.Width + Padding <= other.X || other.X + other.Width + Padding <= slot.X ||
                        slot.Y + slot.Height + Padding <= other.Y || other.Y + other.Height + Padding <= slot.Y;
                    Assert.That(separated, Is.True, $"Slots {i} and {j} keep the padding");
                }
            }
        }

        /// <summary>Scales a dimension the way the layout does.</summary>
        /// <param name="dimension">The source dimension.</param>
        /// <param name="scale">The layout scale.</param>
        /// <param name="limit">The maximum atlas size.</param>
        /// <returns>The packed dimension.</returns>
        private static int Scaled(int dimension, double scale, int limit)
        {
            return scale >= 1.0 ? dimension : Math.Clamp((int)Math.Round(dimension * scale), 1, limit);
        }
    }
}
