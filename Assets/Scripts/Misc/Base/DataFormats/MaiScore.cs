using MajdataPlay.Collections;
using MajdataPlay.Scenes.Game;
using System;

#nullable enable
namespace MajdataPlay
{
    /// <summary>Stores local achievements for one normalized difficulty chart, identified by its level hash.</summary>
    public class MaiScore
    {
        /// <summary>Gets or sets the best DX and classic accuracy.</summary>
        public Accurate Acc { get; set; }
        /// <summary>Gets or sets the best DX score.</summary>
        public long DXScore { get; set; }
        /// <summary>Gets or sets the maximum possible DX score of the latest play.</summary>
        public long TotalDXScore { get; set; }
        /// <summary>Gets or sets the fast judgment count of the latest play.</summary>
        public long Fast { get; set; }
        /// <summary>Gets or sets the late judgment count of the latest play.</summary>
        public long Late { get; set; }
        /// <summary>Gets the normalized difficulty chart's SHA-256 hash, or an empty value before it is known.</summary>
        public string LevelHash { get; init; } = string.Empty;
        /// <summary>Gets or sets the number of plays represented by this record.</summary>
        public long PlayCount { get; set; }
        /// <summary>Gets or sets the detailed judgments of the latest play.</summary>
        public JudgeDetail? JudgeDetail { get; set; }
        /// <summary>Gets or sets the local time of the latest play.</summary>
        public DateTime Timestamp { get; set; }
        /// <summary>Gets or sets the best combo state.</summary>
        public ComboState ComboState { get; set; }

        /// <summary>Copies a completed game into a local score record without incrementing the stored play count.</summary>
        /// <param name="result">The completed game and its judgments.</param>
        /// <param name="level">The difficulty whose level hash identifies the record.</param>
        /// <returns>A score containing this play's values.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The difficulty is outside the supported seven levels.</exception>
        public static MaiScore CreateFromResult(in GameResult result, ChartLevel level)
        {
            return new MaiScore
            {
                LevelHash = result.SongDetail.GetLevelHash(level) ?? string.Empty,
                Acc = result.Acc,
                DXScore = result.DXScore,
                TotalDXScore = result.TotalDXScore,
                JudgeDetail = result.JudgeRecord,
                Fast = result.Fast,
                Late = result.Late,
                ComboState = result.ComboState,
                Timestamp = DateTime.Now,
            };
        }
    }
}
