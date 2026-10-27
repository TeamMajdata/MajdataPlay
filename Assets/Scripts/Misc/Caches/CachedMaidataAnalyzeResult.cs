using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
#nullable enable
namespace MajdataPlay
{
    internal readonly struct CachedMaidataAnalyzeResult
    {
        public bool IsValid { get; init; }
        public TimeSpan Length { get; init; }
        public float MaxBPM { get; init; }
        public float MinBPM { get; init; }
        public DateTime Timestamp { get; init; }
    }
}
