using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using MajdataPlay.Scenes.Game.Notes.Skins;

#nullable enable
namespace AtlasLayoutValidation
{
    /// <summary>
    /// Validates the production skin atlas layout without Unity.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Holds the padding used by the production AtlasBuilder.
        /// </summary>
        private const int Padding = 2;

        /// <summary>
        /// Runs every check.
        /// </summary>
        /// <param name="arguments">An optional skin directory; defaults to the repository's default skin.</param>
        /// <returns>Zero when every check passes; otherwise one.</returns>
        public static int Main(string[] arguments)
        {
            try
            {
                var skinDirectory = arguments.Length > 0
                    ? arguments[0]
                    : Path.Combine(FindRepositoryRoot(), "Assets", "StreamingAssets", "Skins", "default");
                CheckSkinDirectory(skinDirectory);
                CheckRandomSets();
                CheckOversizedImage();
                CheckShrinking();
                CheckSmallestInput();
                CheckDeterminism();
                CheckArguments();
                Console.WriteLine("ATLAS_LAYOUT_VALIDATION_PASSED");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        /// <summary>
        /// Lays out a real skin's atlas images and compares the atlas with the previous 8192 by 4096 result.
        /// </summary>
        /// <param name="skinDirectory">The skin directory.</param>
        /// <exception cref="InvalidOperationException">A check fails.</exception>
        private static void CheckSkinDirectory(string skinDirectory)
        {
            var sizes = new List<(int Width, int Height)>();
            long sourcePixels = 0;
            foreach (var path in Directory.EnumerateFiles(skinDirectory, "*.png", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(path);
                // CustomSkin loads these two as independent textures, not atlas sprites.
                if (name == "SubBackgourd.png" || name == "now_loading.png")
                {
                    continue;
                }
                var size = ReadPngSize(path);
                sizes.Add(size);
                sourcePixels += (long)size.Width * size.Height;
            }
            Check(sizes.Count > 0, $"The skin directory '{skinDirectory}' contains atlas images.");

            var layout = AtlasLayout.Create(sizes, Padding, 8192);
            Validate(sizes, layout, 8192);
            Check(layout.Scale == 1.0, "The skin fits at full size.");
            var atlasPixels = (long)layout.Width * layout.Height;
            Check(atlasPixels < 8192L * 4096L, "The atlas is smaller than the previous PackTextures atlas.");
            Console.WriteLine($"ATLAS_LAYOUT_SKIN images={sizes.Count} atlas={layout.Width}x{layout.Height} " +
                $"baseMiB={atlasPixels * 4 / 1048576.0:F1} utilization={(double)sourcePixels / atlasPixels:P1}");
        }

        /// <summary>
        /// Packs seeded random image sets under several device limits.
        /// </summary>
        /// <exception cref="InvalidOperationException">A check fails.</exception>
        private static void CheckRandomSets()
        {
            var limits = new[] { 2048, 4096, 8192 };
            for (var seed = 1; seed <= 60; seed++)
            {
                var random = new Random(seed);
                var count = random.Next(1, 300);
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

        /// <summary>
        /// Requires an image above the limit to scale the whole set uniformly to the limit.
        /// </summary>
        /// <exception cref="InvalidOperationException">A check fails.</exception>
        private static void CheckOversizedImage()
        {
            var sizes = new List<(int Width, int Height)> { (10000, 50), (200, 100) };
            var layout = AtlasLayout.Create(sizes, Padding, 8192);
            Validate(sizes, layout, 8192);
            Check(Math.Abs(layout.Scale - 0.8192) < 1e-9, "The oversized image sets the uniform scale.");
            Check(layout[0].Width == 8192 && layout[0].Height == 41, "The oversized image keeps its aspect ratio.");
            Check(layout[1].Width == 164 && layout[1].Height == 82, "Other images use the same scale.");
        }

        /// <summary>
        /// Requires a set larger than the atlas to shrink uniformly until it fits.
        /// </summary>
        /// <exception cref="InvalidOperationException">A check fails.</exception>
        private static void CheckShrinking()
        {
            var sizes = new List<(int Width, int Height)>();
            for (var i = 0; i < 100; i++)
            {
                sizes.Add((1000, 1000));
            }
            var layout = AtlasLayout.Create(sizes, Padding, 2048);
            Validate(sizes, layout, 2048);
            Check(layout.Scale < 1.0, "An oversubscribed set shrinks.");
        }

        /// <summary>
        /// Requires a single pixel to produce the smallest aligned atlas.
        /// </summary>
        /// <exception cref="InvalidOperationException">A check fails.</exception>
        private static void CheckSmallestInput()
        {
            var sizes = new List<(int Width, int Height)> { (1, 1) };
            var layout = AtlasLayout.Create(sizes, Padding, 8192);
            Validate(sizes, layout, 8192);
            Check(layout.Width == 4 && layout.Height == 4, "A single pixel uses a 4 by 4 atlas.");
        }

        /// <summary>
        /// Requires identical input to produce identical layouts.
        /// </summary>
        /// <exception cref="InvalidOperationException">A check fails.</exception>
        private static void CheckDeterminism()
        {
            var random = new Random(1234);
            var sizes = new List<(int Width, int Height)>();
            for (var i = 0; i < 150; i++)
            {
                sizes.Add((random.Next(1, 600), random.Next(1, 600)));
            }
            var first = AtlasLayout.Create(sizes, Padding, 4096);
            var second = AtlasLayout.Create(sizes, Padding, 4096);
            Check(first.Width == second.Width && first.Height == second.Height, "Repeated layouts have the same size.");
            for (var i = 0; i < sizes.Count; i++)
            {
                Check(first[i].X == second[i].X && first[i].Y == second[i].Y, "Repeated layouts place images identically.");
            }
        }

        /// <summary>
        /// Requires invalid arguments to be rejected.
        /// </summary>
        /// <exception cref="InvalidOperationException">A check fails.</exception>
        private static void CheckArguments()
        {
            var valid = new List<(int Width, int Height)> { (4, 4) };
            Throws<ArgumentException>(() => AtlasLayout.Create(new List<(int Width, int Height)>(), Padding, 8192), "Empty input");
            Throws<ArgumentException>(() => AtlasLayout.Create(new List<(int Width, int Height)> { (0, 4) }, Padding, 8192), "Zero width");
            Throws<ArgumentOutOfRangeException>(() => AtlasLayout.Create(valid, -1, 8192), "Negative padding");
            Throws<ArgumentOutOfRangeException>(() => AtlasLayout.Create(valid, Padding, 0), "Zero maximum");
        }

        /// <summary>
        /// Checks bounds, slot sizes, alignment and the padding between every pair of slots.
        /// </summary>
        /// <param name="sizes">The source sizes.</param>
        /// <param name="layout">The layout under test.</param>
        /// <param name="limit">The maximum atlas size.</param>
        /// <exception cref="InvalidOperationException">A check fails.</exception>
        private static void Validate(List<(int Width, int Height)> sizes, AtlasLayout layout, int limit)
        {
            Check(layout.Count == sizes.Count, "Every image has a slot.");
            Check(layout.Width > 0 && layout.Height > 0 && layout.Width <= limit && layout.Height <= limit,
                $"Atlas {layout.Width}x{layout.Height} fits the {limit} limit.");
            Check(layout.Width % 4 == 0 && layout.Height % 4 == 0, "Atlas dimensions are 4-aligned.");
            Check(layout.Scale > 0 && layout.Scale <= 1, "The scale is in (0, 1].");
            for (var i = 0; i < sizes.Count; i++)
            {
                var slot = layout[i];
                Check(slot.Width == Scaled(sizes[i].Width, layout.Scale, limit) && slot.Height == Scaled(sizes[i].Height, layout.Scale, limit),
                    $"Slot {i} uses the uniformly scaled size.");
                Check(slot.X >= 0 && slot.Y >= 0 && slot.X + slot.Width <= layout.Width && slot.Y + slot.Height <= layout.Height,
                    $"Slot {i} lies inside the atlas.");
            }
            for (var i = 0; i < sizes.Count; i++)
            {
                var a = layout[i];
                for (var j = i + 1; j < sizes.Count; j++)
                {
                    var b = layout[j];
                    var separated = a.X + a.Width + Padding <= b.X || b.X + b.Width + Padding <= a.X ||
                        a.Y + a.Height + Padding <= b.Y || b.Y + b.Height + Padding <= a.Y;
                    Check(separated, $"Slots {i} and {j} keep {Padding} pixels of padding.");
                }
            }
        }

        /// <summary>
        /// Mirrors the layout's dimension scaling.
        /// </summary>
        /// <param name="dimension">The source dimension.</param>
        /// <param name="scale">The uniform scale.</param>
        /// <param name="limit">The maximum atlas size.</param>
        /// <returns>The expected slot dimension.</returns>
        private static int Scaled(int dimension, double scale, int limit)
        {
            return scale >= 1.0 ? dimension : Math.Clamp((int)Math.Round(dimension * scale), 1, limit);
        }

        /// <summary>
        /// Reads a PNG's dimensions from its IHDR chunk.
        /// </summary>
        /// <param name="path">The PNG path.</param>
        /// <returns>The width and height in pixels.</returns>
        /// <exception cref="InvalidDataException">The file is not a PNG.</exception>
        private static (int Width, int Height) ReadPngSize(string path)
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[24];
            stream.ReadExactly(header);
            if (header[12] != 'I' || header[13] != 'H' || header[14] != 'D' || header[15] != 'R')
            {
                throw new InvalidDataException($"'{path}' is not a PNG.");
            }
            return (BinaryPrimitives.ReadInt32BigEndian(header.Slice(16)), BinaryPrimitives.ReadInt32BigEndian(header.Slice(20)));
        }

        /// <summary>
        /// Finds the repository root from the working directory or the executable location.
        /// </summary>
        /// <returns>The repository root.</returns>
        /// <exception cref="DirectoryNotFoundException">No ancestor contains ProjectSettings/ProjectVersion.txt.</exception>
        private static string FindRepositoryRoot()
        {
            foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            {
                for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                {
                    if (File.Exists(Path.Combine(directory.FullName, "ProjectSettings", "ProjectVersion.txt")))
                    {
                        return directory.FullName;
                    }
                }
            }
            throw new DirectoryNotFoundException("Run from inside the repository or pass a skin directory.");
        }

        /// <summary>
        /// Requires an action to throw a specific exception type.
        /// </summary>
        /// <typeparam name="T">The expected exception type.</typeparam>
        /// <param name="action">The action to run.</param>
        /// <param name="description">The case being checked.</param>
        /// <exception cref="InvalidOperationException">The action did not throw the expected type.</exception>
        private static void Throws<T>(Action action, string description) where T : Exception
        {
            try
            {
                action();
            }
            catch (T exception) when (exception.GetType() == typeof(T))
            {
                return;
            }
            throw new InvalidOperationException($"{description} must throw {typeof(T).Name}.");
        }

        /// <summary>
        /// Requires a validation condition to hold.
        /// </summary>
        /// <param name="condition">The condition to verify.</param>
        /// <param name="message">The failure explanation.</param>
        /// <exception cref="InvalidOperationException">The condition is false.</exception>
        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
