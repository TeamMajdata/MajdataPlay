using MajdataPlay.Utils.ChartRadar;
using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataPlay.Caches
{
    internal readonly struct CachedRadarAnalyzeResult
    {
        public bool IsValid { get; init; }
        public ChartRadarSnapshot Snapshot { get; init; }
        public ReadOnlyMemory<float> Scores { get; init; }
        public DateTime Timestamp { get; init; }
    }
}
