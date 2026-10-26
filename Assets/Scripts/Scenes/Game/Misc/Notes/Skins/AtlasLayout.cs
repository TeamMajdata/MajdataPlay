using System;
using System.Collections.Generic;

#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Skins
{
    /// <summary>
    /// Describes where one image is placed inside a texture atlas, in atlas pixels.
    /// </summary>
    internal readonly struct AtlasSlot
    {
        /// <summary>
        /// Gets the left edge of the image in atlas pixels.
        /// </summary>
        public int X { get; }

        /// <summary>
        /// Gets the bottom edge of the image in atlas pixels.
        /// </summary>
        public int Y { get; }

        /// <summary>
        /// Gets the packed image width in atlas pixels.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the packed image height in atlas pixels.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Creates a slot.
        /// </summary>
        /// <param name="x">The left edge in atlas pixels.</param>
        /// <param name="y">The bottom edge in atlas pixels.</param>
        /// <param name="width">The packed width in atlas pixels.</param>
        /// <param name="height">The packed height in atlas pixels.</param>
        public AtlasSlot(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// Computes a compact atlas layout from image dimensions alone, so that the atlas can be
    /// allocated once and then filled one decoded image at a time.
    /// </summary>
    internal sealed class AtlasLayout
    {
        /// <summary>
        /// Holds the spacing between candidate atlas widths.
        /// </summary>
        private const int WidthStep = 256;

        /// <summary>
        /// Holds the alignment applied to atlas dimensions.
        /// </summary>
        private const int DimensionAlignment = 4;

        /// <summary>
        /// Holds the factor applied to every image when the set does not fit the maximum size.
        /// </summary>
        private const double ShrinkFactor = 0.9;

        /// <summary>
        /// Holds the number of shrink attempts before packing is abandoned.
        /// </summary>
        private const int MaximumShrinkAttempts = 64;

        /// <summary>
        /// Holds the slots in input order.
        /// </summary>
        private readonly AtlasSlot[] _slots;

        /// <summary>
        /// Gets the atlas width in pixels.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the atlas height in pixels.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets the uniform source-to-atlas scale; it is below one only when the images cannot fit at full size.
        /// </summary>
        public double Scale { get; }

        /// <summary>
        /// Gets the number of placed images.
        /// </summary>
        public int Count => _slots.Length;

        /// <summary>
        /// Gets the slot of the image at an input index.
        /// </summary>
        /// <param name="index">The image's index in the sizes passed to <see cref="Create"/>.</param>
        /// <returns>The image's slot.</returns>
        /// <exception cref="IndexOutOfRangeException">The index is outside the input range.</exception>
        public AtlasSlot this[int index] => _slots[index];

        /// <summary>
        /// Creates a layout.
        /// </summary>
        /// <param name="width">The atlas width in pixels.</param>
        /// <param name="height">The atlas height in pixels.</param>
        /// <param name="scale">The uniform source-to-atlas scale.</param>
        /// <param name="slots">The slots in input order.</param>
        private AtlasLayout(int width, int height, double scale, AtlasSlot[] slots)
        {
            Width = width;
            Height = height;
            Scale = scale;
            _slots = slots;
        }

        /// <summary>
        /// Packs images into the smallest atlas found within the maximum size, shrinking every image
        /// by the same factor when they cannot fit at full size.
        /// </summary>
        /// <param name="sizes">The source width and height of each image, in pixels.</param>
        /// <param name="padding">The transparent gap kept between neighbouring images, in atlas pixels.</param>
        /// <param name="maximumSize">The largest allowed atlas width and height, in pixels.</param>
        /// <returns>The layout, with slots in the same order as <paramref name="sizes"/>.</returns>
        /// <exception cref="ArgumentException">The sizes are empty or contain a non-positive dimension.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The padding is negative or the maximum size is not positive.</exception>
        /// <exception cref="InvalidOperationException">The images cannot be packed even after shrinking.</exception>
        public static AtlasLayout Create(IReadOnlyList<(int Width, int Height)> sizes, int padding, int maximumSize)
        {
            if (padding < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(padding), "Padding must not be negative.");
            }
            if (maximumSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSize), "The maximum atlas size must be positive.");
            }
            if (sizes.Count == 0)
            {
                throw new ArgumentException("At least one image is required.", nameof(sizes));
            }

            var longestSide = 0;
            foreach (var size in sizes)
            {
                if (size.Width <= 0 || size.Height <= 0)
                {
                    throw new ArgumentException("Image dimensions must be positive.", nameof(sizes));
                }
                longestSide = Math.Max(longestSide, Math.Max(size.Width, size.Height));
            }

            // Any image above the limit resizes the entire set by the same ratio, preserving relative sizes.
            var scale = Math.Min(1.0, (double)maximumSize / longestSide);
            var scaledWidths = new int[sizes.Count];
            var scaledHeights = new int[sizes.Count];
            for (var attempt = 0; attempt < MaximumShrinkAttempts; attempt++)
            {
                for (var i = 0; i < sizes.Count; i++)
                {
                    scaledWidths[i] = ScaleDimension(sizes[i].Width, scale, maximumSize);
                    scaledHeights[i] = ScaleDimension(sizes[i].Height, scale, maximumSize);
                }
                var layout = TryPack(scaledWidths, scaledHeights, padding, maximumSize, scale);
                if (layout is not null)
                {
                    return layout;
                }
                scale *= ShrinkFactor;
            }
            throw new InvalidOperationException($"{sizes.Count} images cannot be packed into a {maximumSize}-pixel atlas.");
        }

        /// <summary>
        /// Scales one source dimension, keeping it between one pixel and the maximum size.
        /// </summary>
        /// <param name="dimension">The source dimension in pixels.</param>
        /// <param name="scale">The uniform scale.</param>
        /// <param name="maximumSize">The largest allowed dimension.</param>
        /// <returns>The scaled dimension in pixels.</returns>
        private static int ScaleDimension(int dimension, double scale, int maximumSize)
        {
            if (scale >= 1.0)
            {
                return dimension;
            }
            return Math.Clamp((int)Math.Round(dimension * scale), 1, maximumSize);
        }

        /// <summary>
        /// Tries each candidate atlas width and keeps the placement with the smallest area.
        /// </summary>
        /// <param name="widths">The packed image widths.</param>
        /// <param name="heights">The packed image heights.</param>
        /// <param name="padding">The gap between neighbouring images.</param>
        /// <param name="maximumSize">The largest allowed atlas width and height.</param>
        /// <param name="scale">The scale that produced the packed sizes.</param>
        /// <returns>The best layout, or null when no candidate width fits within the maximum height.</returns>
        private static AtlasLayout? TryPack(int[] widths, int[] heights, int padding, int maximumSize, double scale)
        {
            var count = widths.Length;
            var order = new int[count];
            var minimumWidth = 0;
            for (var i = 0; i < count; i++)
            {
                order[i] = i;
                minimumWidth = Math.Max(minimumWidth, widths[i]);
            }
            // Tallest first keeps the skyline flat; the index tie-break keeps the result deterministic.
            Array.Sort(order, (left, right) =>
            {
                var comparison = heights[right].CompareTo(heights[left]);
                if (comparison == 0)
                {
                    comparison = widths[right].CompareTo(widths[left]);
                }
                return comparison == 0 ? left.CompareTo(right) : comparison;
            });

            var skyline = new Skyline(count);
            var candidate = new AtlasSlot[count];
            AtlasSlot[]? best = null;
            var bestWidth = 0;
            var bestHeight = 0;
            var atlasWidth = Math.Min(maximumSize, AlignUp(minimumWidth, DimensionAlignment));
            while (true)
            {
                var usedHeight = skyline.Pack(atlasWidth, widths, heights, order, padding, candidate);
                if (usedHeight <= maximumSize)
                {
                    var atlasHeight = Math.Min(maximumSize, AlignUp(usedHeight, DimensionAlignment));
                    if (best is null || IsBetter(atlasWidth, atlasHeight, bestWidth, bestHeight))
                    {
                        best ??= new AtlasSlot[count];
                        Array.Copy(candidate, best, count);
                        bestWidth = atlasWidth;
                        bestHeight = atlasHeight;
                    }
                }
                if (atlasWidth >= maximumSize)
                {
                    break;
                }
                atlasWidth = Math.Min(maximumSize, (atlasWidth / WidthStep + 1) * WidthStep);
            }
            return best is null ? null : new AtlasLayout(bestWidth, bestHeight, scale, best);
        }

        /// <summary>
        /// Prefers the smaller area, then the squarer atlas.
        /// </summary>
        /// <param name="width">The candidate width.</param>
        /// <param name="height">The candidate height.</param>
        /// <param name="bestWidth">The current best width.</param>
        /// <param name="bestHeight">The current best height.</param>
        /// <returns>Whether the candidate is better than the current best.</returns>
        private static bool IsBetter(int width, int height, int bestWidth, int bestHeight)
        {
            var area = (long)width * height;
            var bestArea = (long)bestWidth * bestHeight;
            if (area != bestArea)
            {
                return area < bestArea;
            }
            return Math.Max(width, height) < Math.Max(bestWidth, bestHeight);
        }

        /// <summary>
        /// Rounds a value up to a multiple of an alignment.
        /// </summary>
        /// <param name="value">The non-negative value.</param>
        /// <param name="alignment">The positive alignment.</param>
        /// <returns>The smallest multiple of the alignment not below the value.</returns>
        private static int AlignUp(int value, int alignment)
        {
            return (value + alignment - 1) / alignment * alignment;
        }

        /// <summary>
        /// Places rectangles with the skyline bottom-left heuristic.
        /// </summary>
        private sealed class Skyline
        {
            /// <summary>
            /// Holds the left edge of each skyline segment.
            /// </summary>
            private readonly int[] _x;

            /// <summary>
            /// Holds the top of each skyline segment.
            /// </summary>
            private readonly int[] _y;

            /// <summary>
            /// Holds the width of each skyline segment.
            /// </summary>
            private readonly int[] _width;

            /// <summary>
            /// Holds the number of live segments.
            /// </summary>
            private int _count;

            /// <summary>
            /// Creates a skyline with room for every segment the placements can produce.
            /// </summary>
            /// <param name="rectangleCount">The number of rectangles that will be placed.</param>
            public Skyline(int rectangleCount)
            {
                var capacity = rectangleCount * 2 + 2;
                _x = new int[capacity];
                _y = new int[capacity];
                _width = new int[capacity];
            }

            /// <summary>
            /// Places every rectangle in a bin of the given width with unbounded height.
            /// </summary>
            /// <param name="atlasWidth">The atlas width in pixels.</param>
            /// <param name="widths">The rectangle widths.</param>
            /// <param name="heights">The rectangle heights.</param>
            /// <param name="order">The placement order.</param>
            /// <param name="padding">The gap between neighbouring rectangles.</param>
            /// <param name="slots">Receives each rectangle's slot by input index.</param>
            /// <returns>The height used by the placed rectangles, or <see cref="int.MaxValue"/> when one is wider than the bin.</returns>
            public int Pack(int atlasWidth, int[] widths, int[] heights, int[] order, int padding, AtlasSlot[] slots)
            {
                // Each rectangle reserves its padding to the right and above; the bin is widened by
                // the padding so that rectangles may touch the atlas's right edge.
                var binWidth = atlasWidth + padding;
                _x[0] = 0;
                _y[0] = 0;
                _width[0] = binWidth;
                _count = 1;
                var usedHeight = 0;
                foreach (var index in order)
                {
                    var cellWidth = widths[index] + padding;
                    var cellHeight = heights[index] + padding;
                    if (!TryFindPosition(cellWidth, cellHeight, binWidth, out var segment, out var y))
                    {
                        return int.MaxValue;
                    }
                    var x = _x[segment];
                    slots[index] = new AtlasSlot(x, y, widths[index], heights[index]);
                    usedHeight = Math.Max(usedHeight, y + heights[index]);
                    Raise(segment, x, y + cellHeight, cellWidth);
                }
                return usedHeight;
            }

            /// <summary>
            /// Finds the segment whose placement keeps the rectangle's top lowest, then leftmost.
            /// </summary>
            /// <param name="cellWidth">The rectangle width including padding.</param>
            /// <param name="cellHeight">The rectangle height including padding.</param>
            /// <param name="binWidth">The bin width including padding.</param>
            /// <param name="segment">Receives the segment where the rectangle's left edge starts.</param>
            /// <param name="y">Receives the rectangle's bottom edge.</param>
            /// <returns>Whether any position fits horizontally.</returns>
            private bool TryFindPosition(int cellWidth, int cellHeight, int binWidth, out int segment, out int y)
            {
                segment = -1;
                y = 0;
                var bestTop = long.MaxValue;
                for (var i = 0; i < _count && _x[i] + cellWidth <= binWidth; i++)
                {
                    var bottom = 0;
                    var remaining = cellWidth;
                    for (var j = i; remaining > 0; j++)
                    {
                        bottom = Math.Max(bottom, _y[j]);
                        remaining -= _width[j];
                    }
                    var top = (long)bottom + cellHeight;
                    if (top < bestTop)
                    {
                        bestTop = top;
                        segment = i;
                        y = bottom;
                    }
                }
                return segment >= 0;
            }

            /// <summary>
            /// Raises the skyline under a newly placed rectangle and merges equal neighbours.
            /// </summary>
            /// <param name="segment">The segment where the rectangle's left edge starts.</param>
            /// <param name="x">The rectangle's left edge.</param>
            /// <param name="top">The rectangle's top edge including padding.</param>
            /// <param name="cellWidth">The rectangle width including padding.</param>
            private void Raise(int segment, int x, int top, int cellWidth)
            {
                Insert(segment, x, top, cellWidth);
                var right = x + cellWidth;
                var next = segment + 1;
                while (next < _count && _x[next] < right)
                {
                    var overlap = right - _x[next];
                    if (overlap >= _width[next])
                    {
                        RemoveAt(next);
                    }
                    else
                    {
                        _x[next] += overlap;
                        _width[next] -= overlap;
                        break;
                    }
                }
                for (var i = 0; i < _count - 1;)
                {
                    if (_y[i] == _y[i + 1])
                    {
                        _width[i] += _width[i + 1];
                        RemoveAt(i + 1);
                    }
                    else
                    {
                        i++;
                    }
                }
            }

            /// <summary>
            /// Inserts a segment before an index.
            /// </summary>
            /// <param name="index">The insertion index.</param>
            /// <param name="x">The segment's left edge.</param>
            /// <param name="y">The segment's top.</param>
            /// <param name="width">The segment's width.</param>
            private void Insert(int index, int x, int y, int width)
            {
                var moved = _count - index;
                Array.Copy(_x, index, _x, index + 1, moved);
                Array.Copy(_y, index, _y, index + 1, moved);
                Array.Copy(_width, index, _width, index + 1, moved);
                _x[index] = x;
                _y[index] = y;
                _width[index] = width;
                _count++;
            }

            /// <summary>
            /// Removes the segment at an index.
            /// </summary>
            /// <param name="index">The removal index.</param>
            private void RemoveAt(int index)
            {
                var moved = _count - index - 1;
                Array.Copy(_x, index + 1, _x, index, moved);
                Array.Copy(_y, index + 1, _y, index, moved);
                Array.Copy(_width, index + 1, _width, index, moved);
                _count--;
            }
        }
    }
}
