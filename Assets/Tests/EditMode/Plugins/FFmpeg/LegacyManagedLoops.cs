using System;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Keeps the scalar managed loops that the Burst kernels replaced, as exact references and benchmark baselines.</summary>
    internal static unsafe class LegacyManagedLoops
    {
        /// <summary>Sets the number of source rows in one quarter-turn copy tile of the replaced loop.</summary>
        private const int RotationTileRows = 32;
        /// <summary>Sets the number of source columns in one quarter-turn copy tile of the replaced loop.</summary>
        private const int RotationTileColumns = 32;

        /// <summary>Copies a range of RGBA source lines through a quarter-turn rotation and Unity's vertical coordinate flip.</summary>
        /// <param name="source">The top-down RGBA source pixels.</param>
        /// <param name="sourceStride">The source row stride in bytes.</param>
        /// <param name="destination">The tightly packed RGBA output buffer.</param>
        /// <param name="width">The source frame width in pixels.</param>
        /// <param name="height">The source frame height in pixels.</param>
        /// <param name="turns">The clockwise rotation in quarter turns.</param>
        /// <param name="firstLine">The first source row (0 or 180 degrees) or source column (90 or 270 degrees) to copy.</param>
        /// <param name="endLine">The source row or column after the last one to copy.</param>
        internal static void CopyRgbaLines(byte* source, int sourceStride, uint* destination, int width, int height, int turns,
            int firstLine, int endLine)
        {
            if ((turns & 1) != 0)
            {
                TransposeColumns(source, sourceStride, destination, width, height, turns, firstLine, endLine);
                return;
            }

            var rowBytes = width * 4;
            for (var y = firstLine; y < endLine; y++)
            {
                var row = (uint*)(source + ((long)y * sourceStride));
                if (turns == 0)
                {
                    Buffer.MemoryCopy(row, destination + ((long)(height - 1 - y) * width), rowBytes, rowBytes);
                }
                else
                {
                    var target = destination + ((long)y * width) + (width - 1);
                    for (var x = 0; x < width; x++)
                    {
                        target[-x] = row[x];
                    }
                }
            }
        }

        /// <summary>Converts float samples to signed 16-bit PCM with the replaced branching loop.</summary>
        /// <param name="source">The float samples.</param>
        /// <param name="destination">The 16-bit samples, at least as long as the source.</param>
        internal static void ConvertToPcm16(ReadOnlySpan<float> source, Span<short> destination)
        {
            for (var i = 0; i < source.Length; i++)
            {
                var scaled = source[i] * 32767f;
                if (float.IsNaN(scaled))
                {
                    destination[i] = 0;
                }
                else if (scaled >= short.MaxValue)
                {
                    destination[i] = short.MaxValue;
                }
                else if (scaled <= short.MinValue)
                {
                    destination[i] = short.MinValue;
                }
                else
                {
                    destination[i] = (short)MathF.Round(scaled);
                }
            }
        }

        /// <summary>Copies a range of source columns through a 90 or 270 degree turn and Unity's vertical flip in tiles.</summary>
        /// <param name="source">The top-down RGBA source pixels.</param>
        /// <param name="sourceStride">The source row stride in bytes.</param>
        /// <param name="destination">The tightly packed RGBA output buffer.</param>
        /// <param name="width">The source frame width in pixels.</param>
        /// <param name="height">The source frame height in pixels.</param>
        /// <param name="turns">The clockwise rotation in quarter turns, either 1 or 3.</param>
        /// <param name="firstColumn">The first source column to copy.</param>
        /// <param name="endColumn">The source column after the last column to copy.</param>
        private static void TransposeColumns(byte* source, int sourceStride, uint* destination, int width, int height,
            int turns, int firstColumn, int endColumn)
        {
            for (var tileRow = 0; tileRow < height; tileRow += RotationTileRows)
            {
                var tileRowEnd = Math.Min(height, tileRow + RotationTileRows);
                for (var tileColumn = firstColumn; tileColumn < endColumn; tileColumn += RotationTileColumns)
                {
                    var tileColumnEnd = Math.Min(endColumn, tileColumn + RotationTileColumns);
                    for (var x = tileColumn; x < tileColumnEnd; x++)
                    {
                        var column = source + ((long)x * 4);
                        if (turns == 1)
                        {
                            var target = destination + ((long)(width - 1 - x) * height) + (height - 1);
                            for (var y = tileRow; y < tileRowEnd; y++)
                            {
                                target[-y] = *(uint*)(column + ((long)y * sourceStride));
                            }
                        }
                        else
                        {
                            var target = destination + ((long)x * height);
                            for (var y = tileRow; y < tileRowEnd; y++)
                            {
                                target[y] = *(uint*)(column + ((long)y * sourceStride));
                            }
                        }
                    }
                }
            }
        }
    }
}
