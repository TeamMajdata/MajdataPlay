#nullable enable
using System;
using Unity.Burst;
using Unity.Mathematics;

namespace MajdataPlay.FFmpeg.Internal
{
    /// <summary>Provides the Burst-compiled pixel loops of the CPU video paths: decoded RGBA presentation copies and capture YUV conversion.</summary>
    /// <remarks>
    /// Every entry point is a Burst direct call over borrowed raw pointers, so decoder workers, encoder workers, and
    /// <see cref="VideoBandExecutor"/> band threads call it without touching Unity objects. Distinct line or row-pair ranges
    /// of one image write disjoint output rows and may run concurrently. Without Burst (when it is disabled, or in the
    /// Editor until asynchronous compilation finishes) the same code runs as managed code and produces identical pixels.
    /// </remarks>
    [BurstCompile]
    internal static unsafe class VideoPixelKernels
    {
        /// <summary>Sets the number of source rows in one quarter-turn copy tile, which become contiguous output pixels.</summary>
        private const int RotationTileRows = 32;
        /// <summary>Sets the number of source columns in one quarter-turn copy tile, which become separate output rows.</summary>
        private const int RotationTileColumns = 32;
        /// <summary>Sets the bit offset of the red channel in a little-endian RGBA8 word.</summary>
        private const int RedShift = 0;
        /// <summary>Sets the bit offset of the green channel in a little-endian RGBA8 word.</summary>
        private const int GreenShift = 8;
        /// <summary>Sets the bit offset of the blue channel in a little-endian RGBA8 word.</summary>
        private const int BlueShift = 16;
        /// <summary>Sets the number of fractional bits of the fixed-point luma coefficients.</summary>
        internal const int FixedPointBits = 16;
        /// <summary>Sets the fractional bits of the chroma coefficients, which also divide the sum of four pixels by four.</summary>
        internal const int ChromaBits = FixedPointBits + 2;
        /// <summary>Stores the BT.709 red luma weight.</summary>
        private const double Bt709Red = 0.2126;
        /// <summary>Stores the BT.709 blue luma weight.</summary>
        private const double Bt709Blue = 0.0722;
        /// <summary>Stores the BT.709 green luma weight.</summary>
        private const double Bt709Green = 1.0 - Bt709Red - Bt709Blue;
        /// <summary>Maps full-range 0-255 luma onto the 219 limited-range codes, in fixed point.</summary>
        private const double LumaScale = 219.0 / 255.0 * (1 << FixedPointBits);
        /// <summary>Maps a full-range color difference onto half of the 224 limited-range chroma codes, in fixed point.</summary>
        private const double ChromaScale = 112.0 / 255.0 * (1 << FixedPointBits);
        /// <summary>Stores the fixed-point red weight of limited-range luma.</summary>
        internal const int LumaRed = (int)((Bt709Red * LumaScale) + 0.5);
        /// <summary>Stores the fixed-point green weight of limited-range luma.</summary>
        internal const int LumaGreen = (int)((Bt709Green * LumaScale) + 0.5);
        /// <summary>Stores the fixed-point blue weight of limited-range luma.</summary>
        internal const int LumaBlue = (int)((Bt709Blue * LumaScale) + 0.5);
        /// <summary>Adds the limited-range black level of 16 and half a code for rounding to fixed-point luma.</summary>
        internal const int LumaOffset = (16 << FixedPointBits) + (1 << (FixedPointBits - 1));
        /// <summary>Stores the fixed-point blue weight of the blue difference.</summary>
        internal const int BlueDifferenceBlue = (int)(ChromaScale + 0.5);
        /// <summary>Stores the fixed-point red weight of the blue difference.</summary>
        internal const int BlueDifferenceRed = -(int)((ChromaScale * Bt709Red / (1.0 - Bt709Blue)) + 0.5);
        /// <summary>Stores the fixed-point green weight of the blue difference, chosen so that gray maps exactly to neutral chroma.</summary>
        internal const int BlueDifferenceGreen = -(BlueDifferenceBlue + BlueDifferenceRed);
        /// <summary>Stores the fixed-point red weight of the red difference.</summary>
        internal const int RedDifferenceRed = (int)(ChromaScale + 0.5);
        /// <summary>Stores the fixed-point blue weight of the red difference.</summary>
        internal const int RedDifferenceBlue = -(int)((ChromaScale * Bt709Blue / (1.0 - Bt709Red)) + 0.5);
        /// <summary>Stores the fixed-point green weight of the red difference, chosen so that gray maps exactly to neutral chroma.</summary>
        internal const int RedDifferenceGreen = -(RedDifferenceRed + RedDifferenceBlue);
        /// <summary>Adds the neutral chroma level of 128 and half a code for rounding to fixed-point chroma.</summary>
        internal const int ChromaOffset = (128 << ChromaBits) + (1 << (ChromaBits - 1));

        /// <summary>Copies a range of RGBA source lines through a quarter-turn rotation and Unity's vertical coordinate flip.</summary>
        /// <param name="source">The borrowed top-down RGBA source pixels.</param>
        /// <param name="sourceStride">The source row stride in bytes.</param>
        /// <param name="destination">The writable, tightly packed RGBA output buffer, which does not overlap the source.</param>
        /// <param name="width">The source frame width in pixels.</param>
        /// <param name="height">The source frame height in pixels.</param>
        /// <param name="turns">The clockwise rotation in quarter turns, from 0 to 3.</param>
        /// <param name="firstLine">The first source row (0 or 180 degrees) or source column (90 or 270 degrees) to copy.</param>
        /// <param name="endLine">The source row or column after the last one to copy.</param>
        /// <remarks>Each source line becomes one whole output row, so distinct line ranges may be copied concurrently.</remarks>
        [BurstCompile]
        public static void CopyRgbaLines([NoAlias] byte* source, int sourceStride, [NoAlias] uint* destination, int width,
            int height, int turns, int firstLine, int endLine)
        {
            switch (turns)
            {
                case 0:
                    FlipRows(source, sourceStride, destination, width, height, firstLine, endLine);
                    break;
                case 2:
                    MirrorRows(source, sourceStride, destination, width, firstLine, endLine);
                    break;
                default:
                    TransposeColumns(source, sourceStride, destination, width, height, turns, firstLine, endLine);
                    break;
            }
        }

        /// <summary>Converts output row pairs of packed sRGB RGBA8 pixels to limited-range BT.709 4:2:0 planes.</summary>
        /// <param name="source">The source pixels of output row 0; alpha is ignored.</param>
        /// <param name="sourceStride">The signed byte distance from one output row's source pixels to the next; negative for bottom-first input.</param>
        /// <param name="width">The even image width in pixels.</param>
        /// <param name="planes">The writable destination planes, which do not overlap the source.</param>
        /// <param name="firstPair">The first output row pair to convert.</param>
        /// <param name="endPair">The output row pair after the last one to convert.</param>
        /// <remarks>
        /// Each chroma sample is the rounded mean of its 2x2 pixel block (center siting). The coefficients sum exactly so
        /// that gray input yields neutral chroma. Distinct row-pair ranges write disjoint rows and may run concurrently.
        /// </remarks>
        [BurstCompile]
        public static void ConvertRgbaToYuv420([NoAlias] byte* source, long sourceStride, int width, in Yuv420Planes planes,
            int firstPair, int endPair)
        {
            var luma = planes.Luma;
            var lumaStride = planes.LumaStride;
            var blueDifference = planes.BlueDifference;
            var blueDifferenceStride = planes.BlueDifferenceStride;
            var redDifference = planes.RedDifference;
            var redDifferenceStride = planes.RedDifferenceStride;
            var chromaStep = planes.ChromaStep;
            var vectorWidth = width & ~7;
            for (var pair = firstPair; pair < endPair; pair++)
            {
                var top = source + (2L * pair * sourceStride);
                var bottom = top + sourceStride;
                var lumaTop = luma + (2L * pair * lumaStride);
                var lumaBottom = lumaTop + lumaStride;
                var blueRow = blueDifference + ((long)pair * blueDifferenceStride);
                var redRow = redDifference + ((long)pair * redDifferenceStride);
                var x = 0;
                for (; x < vectorWidth; x += 8)
                {
                    // Eight pixels of both rows produce eight luma samples per row and four chroma samples.
                    var top0 = LoadPixels(top, x);
                    var top1 = LoadPixels(top, x + 4);
                    var bottom0 = LoadPixels(bottom, x);
                    var bottom1 = LoadPixels(bottom, x + 4);
                    StoreBytes(lumaTop + x, Luma(top0));
                    StoreBytes(lumaTop + x + 4, Luma(top1));
                    StoreBytes(lumaBottom + x, Luma(bottom0));
                    StoreBytes(lumaBottom + x + 4, Luma(bottom1));
                    var red = BlockSums(top0, top1, bottom0, bottom1, RedShift);
                    var green = BlockSums(top0, top1, bottom0, bottom1, GreenShift);
                    var blue = BlockSums(top0, top1, bottom0, bottom1, BlueShift);
                    var sample = (long)(x / 2) * chromaStep;
                    StoreChroma(blueRow + sample, redRow + sample, chromaStep, BlueDifferenceOf(red, green, blue),
                        RedDifferenceOf(red, green, blue));
                }

                for (; x < width; x += 2)
                {
                    // Repeating one 2x2 block in every lane reuses the vector formulas for the row tail.
                    var topPixels = *(uint2*)(top + (x * 4L));
                    var bottomPixels = *(uint2*)(bottom + (x * 4L));
                    var topPair = new uint4(topPixels, topPixels);
                    var bottomPair = new uint4(bottomPixels, bottomPixels);
                    var topLuma = Luma(topPair);
                    var bottomLuma = Luma(bottomPair);
                    lumaTop[x] = (byte)topLuma.x;
                    lumaTop[x + 1] = (byte)topLuma.y;
                    lumaBottom[x] = (byte)bottomLuma.x;
                    lumaBottom[x + 1] = (byte)bottomLuma.y;
                    var red = BlockSums(topPair, topPair, bottomPair, bottomPair, RedShift);
                    var green = BlockSums(topPair, topPair, bottomPair, bottomPair, GreenShift);
                    var blue = BlockSums(topPair, topPair, bottomPair, bottomPair, BlueShift);
                    var sample = (long)(x / 2) * chromaStep;
                    blueRow[sample] = (byte)BlueDifferenceOf(red, green, blue).x;
                    redRow[sample] = (byte)RedDifferenceOf(red, green, blue).x;
                }
            }
        }

        /// <summary>Copies a tightly packed NV12 image into 4:2:0 planes, splitting the interleaved chroma for planar destinations.</summary>
        /// <param name="source">The Y plane of <paramref name="width"/> by <paramref name="height"/> bytes, followed by <paramref name="height"/> / 2 rows of interleaved Cb, Cr pairs.</param>
        /// <param name="width">The even image width in pixels.</param>
        /// <param name="height">The even image height in pixels.</param>
        /// <param name="planes">The writable destination planes, which do not overlap the source.</param>
        [BurstCompile]
        public static void CopyNv12ToYuv420([NoAlias] byte* source, int width, int height, in Yuv420Planes planes)
        {
            for (var y = 0; y < height; y++)
            {
                Buffer.MemoryCopy(source + ((long)y * width), planes.Luma + ((long)y * planes.LumaStride), width, width);
            }

            var chroma = source + ((long)width * height);
            var pairs = width / 2;
            for (var row = 0; row < height / 2; row++)
            {
                var input = chroma + ((long)row * width);
                var blueRow = planes.BlueDifference + ((long)row * planes.BlueDifferenceStride);
                if (planes.ChromaStep == 2)
                {
                    // An NV12 destination interleaves its chroma like the source, starting with Cb.
                    Buffer.MemoryCopy(input, blueRow, width, width);
                    continue;
                }

                var redRow = planes.RedDifference + ((long)row * planes.RedDifferenceStride);
                for (var x = 0; x < pairs; x++)
                {
                    blueRow[x] = input[2 * x];
                    redRow[x] = input[(2 * x) + 1];
                }
            }
        }

        /// <summary>Copies source rows to the vertically flipped output rows without rotation.</summary>
        /// <param name="source">The borrowed top-down RGBA source pixels.</param>
        /// <param name="sourceStride">The source row stride in bytes.</param>
        /// <param name="destination">The writable, tightly packed RGBA output buffer.</param>
        /// <param name="width">The source frame width in pixels.</param>
        /// <param name="height">The source frame height in pixels.</param>
        /// <param name="firstRow">The first source row to copy.</param>
        /// <param name="endRow">The source row after the last one to copy.</param>
        private static void FlipRows(byte* source, int sourceStride, uint* destination, int width, int height, int firstRow,
            int endRow)
        {
            var rowBytes = width * 4L;
            for (var y = firstRow; y < endRow; y++)
            {
                // Burst lowers Buffer.MemoryCopy to a native memory copy; unlike UnsafeUtility it also runs outside Unity.
                Buffer.MemoryCopy(source + ((long)y * sourceStride), destination + ((long)(height - 1 - y) * width), rowBytes, rowBytes);
            }
        }

        /// <summary>Copies source rows to the same output rows in reverse pixel order, a 180 degree turn combined with the vertical flip.</summary>
        /// <param name="source">The borrowed top-down RGBA source pixels.</param>
        /// <param name="sourceStride">The source row stride in bytes.</param>
        /// <param name="destination">The writable, tightly packed RGBA output buffer.</param>
        /// <param name="width">The source frame width in pixels.</param>
        /// <param name="firstRow">The first source row to copy.</param>
        /// <param name="endRow">The source row after the last one to copy.</param>
        private static void MirrorRows(byte* source, int sourceStride, uint* destination, int width, int firstRow, int endRow)
        {
            for (var y = firstRow; y < endRow; y++)
            {
                var row = (uint*)(source + ((long)y * sourceStride));
                var target = destination + ((long)y * width);
                var x = 0;
                for (; x <= width - 4; x += 4)
                {
                    // Four pixels reverse their order inside one vector, which lands at the mirrored end of the row.
                    *(uint4*)(target + (width - 4 - x)) = (*(uint4*)(row + x)).wzyx;
                }

                for (; x < width; x++)
                {
                    target[width - 1 - x] = row[x];
                }
            }
        }

        /// <summary>Copies a range of source columns through a 90 or 270 degree turn and Unity's vertical flip in cache-sized tiles.</summary>
        /// <param name="source">The borrowed top-down RGBA source pixels.</param>
        /// <param name="sourceStride">The source row stride in bytes.</param>
        /// <param name="destination">The writable, tightly packed RGBA output buffer.</param>
        /// <param name="width">The source frame width in pixels.</param>
        /// <param name="height">The source frame height in pixels.</param>
        /// <param name="turns">The clockwise rotation in quarter turns, either 1 or 3.</param>
        /// <param name="firstColumn">The first source column to copy.</param>
        /// <param name="endColumn">The source column after the last column to copy.</param>
        /// <remarks>
        /// Each source column becomes one output row. A tile reads a few cache lines from each of its source rows and writes
        /// a contiguous run of every covered output row; inside a tile, 4x4 pixel blocks are transposed in vector registers.
        /// </remarks>
        private static void TransposeColumns(byte* source, int sourceStride, uint* destination, int width, int height,
            int turns, int firstColumn, int endColumn)
        {
            for (var tileRow = 0; tileRow < height; tileRow += RotationTileRows)
            {
                var tileRowEnd = math.min(height, tileRow + RotationTileRows);
                for (var tileColumn = firstColumn; tileColumn < endColumn; tileColumn += RotationTileColumns)
                {
                    var tileColumnEnd = math.min(endColumn, tileColumn + RotationTileColumns);
                    TransposeTile(source, sourceStride, destination, width, height, turns, tileRow, tileRowEnd, tileColumn,
                        tileColumnEnd);
                }
            }
        }

        /// <summary>Transposes one tile, using 4x4 blocks where they fit and single pixels along the tile's right and bottom edges.</summary>
        /// <param name="source">The borrowed top-down RGBA source pixels.</param>
        /// <param name="sourceStride">The source row stride in bytes.</param>
        /// <param name="destination">The writable, tightly packed RGBA output buffer.</param>
        /// <param name="width">The source frame width in pixels.</param>
        /// <param name="height">The source frame height in pixels.</param>
        /// <param name="turns">The clockwise rotation in quarter turns, either 1 or 3.</param>
        /// <param name="firstRow">The first source row of the tile.</param>
        /// <param name="endRow">The source row after the tile.</param>
        /// <param name="firstColumn">The first source column of the tile.</param>
        /// <param name="endColumn">The source column after the tile.</param>
        private static void TransposeTile(byte* source, int sourceStride, uint* destination, int width, int height, int turns,
            int firstRow, int endRow, int firstColumn, int endColumn)
        {
            var blockRowEnd = firstRow + ((endRow - firstRow) & ~3);
            var blockColumnEnd = firstColumn + ((endColumn - firstColumn) & ~3);
            for (var y = firstRow; y < blockRowEnd; y += 4)
            {
                for (var x = firstColumn; x < blockColumnEnd; x += 4)
                {
                    TransposeBlock(source, sourceStride, destination, width, height, turns, x, y);
                }

                for (var x = blockColumnEnd; x < endColumn; x++)
                {
                    for (var row = y; row < y + 4; row++)
                    {
                        TransposePixel(source, sourceStride, destination, width, height, turns, x, row);
                    }
                }
            }

            for (var y = blockRowEnd; y < endRow; y++)
            {
                for (var x = firstColumn; x < endColumn; x++)
                {
                    TransposePixel(source, sourceStride, destination, width, height, turns, x, y);
                }
            }
        }

        /// <summary>Transposes the 4x4 pixel block whose top-left source pixel is at the given column and row.</summary>
        /// <param name="source">The borrowed top-down RGBA source pixels.</param>
        /// <param name="sourceStride">The source row stride in bytes.</param>
        /// <param name="destination">The writable, tightly packed RGBA output buffer.</param>
        /// <param name="width">The source frame width in pixels.</param>
        /// <param name="height">The source frame height in pixels.</param>
        /// <param name="turns">The clockwise rotation in quarter turns, either 1 or 3.</param>
        /// <param name="x">The block's first source column.</param>
        /// <param name="y">The block's first source row.</param>
        private static void TransposeBlock(byte* source, int sourceStride, uint* destination, int width, int height, int turns,
            int x, int y)
        {
            var row = source + ((long)y * sourceStride) + (x * 4L);
            // Columns of the transposed block hold source columns x to x + 3, each from source rows y to y + 3.
            var block = math.transpose(new uint4x4(
                *(uint4*)row,
                *(uint4*)(row + sourceStride),
                *(uint4*)(row + (2L * sourceStride)),
                *(uint4*)(row + (3L * sourceStride))));
            if (turns == 1)
            {
                // The output is height pixels wide; source column x lands on output row (width - 1 - x) in reverse row order.
                var target = destination + ((long)(width - 1 - x) * height) + (height - 4 - y);
                *(uint4*)target = block.c0.wzyx;
                *(uint4*)(target - height) = block.c1.wzyx;
                *(uint4*)(target - (2L * height)) = block.c2.wzyx;
                *(uint4*)(target - (3L * height)) = block.c3.wzyx;
            }
            else
            {
                var target = destination + ((long)x * height) + y;
                *(uint4*)target = block.c0;
                *(uint4*)(target + height) = block.c1;
                *(uint4*)(target + (2L * height)) = block.c2;
                *(uint4*)(target + (3L * height)) = block.c3;
            }
        }

        /// <summary>Copies one source pixel to its rotated and vertically flipped output position.</summary>
        /// <param name="source">The borrowed top-down RGBA source pixels.</param>
        /// <param name="sourceStride">The source row stride in bytes.</param>
        /// <param name="destination">The writable, tightly packed RGBA output buffer.</param>
        /// <param name="width">The source frame width in pixels.</param>
        /// <param name="height">The source frame height in pixels.</param>
        /// <param name="turns">The clockwise rotation in quarter turns, either 1 or 3.</param>
        /// <param name="x">The source column.</param>
        /// <param name="y">The source row.</param>
        private static void TransposePixel(byte* source, int sourceStride, uint* destination, int width, int height, int turns,
            int x, int y)
        {
            var pixel = *(uint*)(source + ((long)y * sourceStride) + (x * 4L));
            if (turns == 1)
            {
                destination[((long)(width - 1 - x) * height) + (height - 1 - y)] = pixel;
            }
            else
            {
                destination[((long)x * height) + y] = pixel;
            }
        }

        /// <summary>Loads four consecutive RGBA8 pixels.</summary>
        /// <param name="row">The first byte of the source row.</param>
        /// <param name="x">The column of the first pixel.</param>
        /// <returns>The four pixels as little-endian RGBA words.</returns>
        private static uint4 LoadPixels(byte* row, int x)
        {
            return *(uint4*)(row + (x * 4L));
        }

        /// <summary>Extracts one 8-bit channel from four RGBA8 pixels.</summary>
        /// <param name="pixels">The little-endian RGBA words.</param>
        /// <param name="shift">The bit offset of the channel.</param>
        /// <returns>The channel values from 0 to 255.</returns>
        private static int4 Channel(uint4 pixels, int shift)
        {
            return (int4)((pixels >> shift) & 0xFFu);
        }

        /// <summary>Computes limited-range BT.709 luma for four pixels.</summary>
        /// <param name="pixels">The little-endian RGBA words.</param>
        /// <returns>The luma codes from 16 to 235.</returns>
        private static int4 Luma(uint4 pixels)
        {
            return ((LumaRed * Channel(pixels, RedShift)) + (LumaGreen * Channel(pixels, GreenShift))
                + (LumaBlue * Channel(pixels, BlueShift)) + LumaOffset) >> FixedPointBits;
        }

        /// <summary>Sums one channel over each of four horizontally adjacent 2x2 pixel blocks.</summary>
        /// <param name="top0">The first four pixels of the upper row.</param>
        /// <param name="top1">The next four pixels of the upper row.</param>
        /// <param name="bottom0">The first four pixels of the lower row.</param>
        /// <param name="bottom1">The next four pixels of the lower row.</param>
        /// <param name="shift">The bit offset of the channel.</param>
        /// <returns>The four block sums from 0 to 1020.</returns>
        private static int4 BlockSums(uint4 top0, uint4 top1, uint4 bottom0, uint4 bottom1, int shift)
        {
            var columns0 = Channel(top0, shift) + Channel(bottom0, shift);
            var columns1 = Channel(top1, shift) + Channel(bottom1, shift);
            return new int4(columns0.xz, columns1.xz) + new int4(columns0.yw, columns1.yw);
        }

        /// <summary>Computes limited-range BT.709 blue-difference chroma from 2x2 block channel sums.</summary>
        /// <param name="red">The red sums of four blocks.</param>
        /// <param name="green">The green sums of four blocks.</param>
        /// <param name="blue">The blue sums of four blocks.</param>
        /// <returns>The chroma codes from 16 to 240.</returns>
        private static int4 BlueDifferenceOf(int4 red, int4 green, int4 blue)
        {
            return ((BlueDifferenceRed * red) + (BlueDifferenceGreen * green) + (BlueDifferenceBlue * blue) + ChromaOffset)
                >> ChromaBits;
        }

        /// <summary>Computes limited-range BT.709 red-difference chroma from 2x2 block channel sums.</summary>
        /// <param name="red">The red sums of four blocks.</param>
        /// <param name="green">The green sums of four blocks.</param>
        /// <param name="blue">The blue sums of four blocks.</param>
        /// <returns>The chroma codes from 16 to 240.</returns>
        private static int4 RedDifferenceOf(int4 red, int4 green, int4 blue)
        {
            return ((RedDifferenceRed * red) + (RedDifferenceGreen * green) + (RedDifferenceBlue * blue) + ChromaOffset)
                >> ChromaBits;
        }

        /// <summary>Stores four 8-bit codes to consecutive bytes.</summary>
        /// <param name="destination">The first destination byte.</param>
        /// <param name="values">The codes, each from 0 to 255.</param>
        private static void StoreBytes(byte* destination, int4 values)
        {
            *(uint*)destination = (uint)values.x | ((uint)values.y << 8) | ((uint)values.z << 16) | ((uint)values.w << 24);
        }

        /// <summary>Stores four horizontally adjacent samples of both chroma components.</summary>
        /// <param name="blueDifference">The first blue-difference destination byte.</param>
        /// <param name="redDifference">The first red-difference destination byte.</param>
        /// <param name="step">The byte distance between adjacent samples of one component.</param>
        /// <param name="blue">The blue-difference codes.</param>
        /// <param name="red">The red-difference codes.</param>
        private static void StoreChroma(byte* blueDifference, byte* redDifference, int step, int4 blue, int4 red)
        {
            if (step == 1)
            {
                StoreBytes(blueDifference, blue);
                StoreBytes(redDifference, red);
                return;
            }

            for (var lane = 0; lane < 4; lane++)
            {
                blueDifference[lane * step] = (byte)blue[lane];
                redDifference[lane * step] = (byte)red[lane];
            }
        }
    }
}
