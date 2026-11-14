using MajdataPlay.Collections;
using MajdataPlay.Scenes.Game;
using System;

#nullable enable
namespace MajdataPlay
{
    /// <summary>Reads scores stored before difficulty charts were identified by level hashes.</summary>
    internal sealed class LegacyMaiScore
    {
        /// <summary>Gets or sets the stored DX and classic accuracy.</summary>
        public Accurate Acc { get; set; }
        /// <summary>Gets or sets the stored best DX score.</summary>
        public long DXScore { get; set; }
        /// <summary>Gets or sets the maximum possible DX score.</summary>
        public long TotalDXScore { get; set; }
        /// <summary>Gets or sets the fast judgment count of the latest play.</summary>
        public long Fast { get; set; }
        /// <summary>Gets or sets the late judgment count of the latest play.</summary>
        public long Late { get; set; }
        /// <summary>Gets or sets the difficulty index paired with the old whole-chart hash.</summary>
        public ChartLevel ChartLevel { get; set; }
        /// <summary>Gets or sets the old hash of the whole chart file.</summary>
        public string? Hash { get; set; }
        /// <summary>Gets or sets the number of plays represented by this record.</summary>
        public long PlayCount { get; set; }
        /// <summary>Gets or sets the detailed judgments of the latest play.</summary>
        public JudgeDetail? JudgeDetail { get; set; }
        /// <summary>Gets or sets the local time of the latest play.</summary>
        public DateTime Timestamp { get; set; }
        /// <summary>Gets or sets the best combo state.</summary>
        public ComboState ComboState { get; set; }
    }
}
