using MajSimai;
using System;

#nullable enable
namespace MajdataPlay
{
    /// <summary>Resolves the content identity of a song's individual chart levels.</summary>
    internal static class SongDetailExtensions
    {
        /// <summary>Gets the normalized inote SHA-256 hash of one chart level.</summary>
        /// <param name="song">The song containing the level.</param>
        /// <param name="level">The requested chart level.</param>
        /// <returns>The level hash, or null when the level is absent or its hash is unknown.</returns>
        /// <exception cref="ArgumentNullException">The song is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The level is outside the seven supported chart levels.</exception>
        public static string? GetLevelHash(this ISongDetail song, ChartLevel level)
        {
            if (song is null)
            {
                throw new ArgumentNullException(nameof(song));
            }
            var index = (int)level;
            if (index is < 0 or > 6)
            {
                throw new ArgumentOutOfRangeException(nameof(level));
            }
            var hashes = song.LevelHashes;
            return index < hashes.Length && !string.IsNullOrEmpty(hashes[index]) ? hashes[index] : null;
        }

        /// <summary>Computes level hashes directly from metadata using the same MajSimai normalization as the server.</summary>
        /// <param name="metadata">The parsed file metadata, including its inote text.</param>
        /// <returns>Seven hashes indexed by level, with null for each level without inote text.</returns>
        internal static string?[] GetLevelHashes(SimaiMetadata metadata)
        {
            var hashes = new string?[7];
            var fumens = metadata.Fumens;
            for (var index = 0; index < hashes.Length && index < fumens.Length; index++)
            {
                var fumen = fumens[index];
                if (!string.IsNullOrEmpty(fumen))
                {
                    var chart = new SimaiChart(string.Empty, string.Empty, fumen, ReadOnlySpan<SimaiTimingPoint>.Empty);
                    hashes[index] = chart.Sha256;
                }
            }
            return hashes;
        }
    }
}
