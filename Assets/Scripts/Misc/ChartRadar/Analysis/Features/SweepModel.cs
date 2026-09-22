using System;
using System.Collections.Generic;
using System.Linq;
using SimaiRadar.Core;

#nullable enable

namespace SimaiRadar.Analysis.Features;

// Internal data passed between the three independently testable Sweep stages.
internal sealed class SweepAttack
{
    internal int Id { get; set; }
    internal int Lane { get; set; }
    internal BeatPosition Beat { get; set; }
    internal double TimeSeconds { get; set; }
    internal IReadOnlyList<int> EventIds { get; set; } = Array.Empty<int>();
    internal int NormalDeclarations { get; set; }
    internal int ProtectedDeclarations { get; set; }
}

internal sealed class SweepStrand
{
    internal IReadOnlyList<int> AttackIds { get; set; } = Array.Empty<int>();
    internal IReadOnlyList<int> Lanes { get; set; } = Array.Empty<int>();
    internal int InitialDirection { get; set; }
    internal int FinalDirection { get; set; }
    internal int TurnCount { get; set; }
}

internal sealed class SweepSequence
{
    internal IReadOnlyList<IReadOnlyList<int>> LanesByBatch { get; set; } =
        Array.Empty<IReadOnlyList<int>>();
    internal IReadOnlyList<double> Times { get; set; } = Array.Empty<double>();
    internal IReadOnlyList<double> UnitTimeIntervals { get; set; } = Array.Empty<double>();
    internal IReadOnlyCollection<int> SpeedSwitches { get; set; } = new HashSet<int>();
    internal IReadOnlyCollection<int> DirectionSwitches { get; set; } = new HashSet<int>();
    internal IReadOnlyCollection<int> WidthSwitches { get; set; } = new HashSet<int>();
    internal IReadOnlyCollection<int> DoubleHandoffs { get; set; } = new HashSet<int>();
    internal IReadOnlyList<int> NormalDeclarations { get; set; } = Array.Empty<int>();
    internal IReadOnlyList<int> ProtectedDeclarations { get; set; } = Array.Empty<int>();
    internal IReadOnlyList<SweepStrand> Strands { get; set; } = Array.Empty<SweepStrand>();
    internal IReadOnlyList<int> Widths { get; set; } = Array.Empty<int>();
    internal BeatPosition StartBeat { get; set; }
    internal BeatPosition EndBeat { get; set; }
    internal int AttackCount { get; set; }
    internal int BatchCount { get; set; }
    internal double MedianIntervalSeconds { get; set; }

    internal double StartTime => Times[0];
    internal double EndTime => Times[^1];
}

internal sealed class ScoredSweepGroup
{
    internal int Id { get; set; }
    internal SweepSequence Sequence { get; set; } = null!;
    internal int? ParentId { get; set; }
}

internal sealed class SweepFamily
{
    internal int Id { get; set; }
    internal IReadOnlyList<int> GroupIds { get; set; } = Array.Empty<int>();
}

internal sealed class HandAssignment
{
    internal double Time { get; set; }
    internal IReadOnlyList<int> LeftLanes { get; set; } = Array.Empty<int>();
    internal IReadOnlyList<int> RightLanes { get; set; } = Array.Empty<int>();
    internal int? LeftPosition { get; set; }
    internal int? RightPosition { get; set; }
    internal int LeftIdleDistance { get; set; }
    internal int RightIdleDistance { get; set; }
    internal int FreeHandTakeover { get; set; }
    internal int FastJumpViolations { get; set; }
}

internal sealed class HandMotionResult
{
    internal int TotalDistance { get; set; }
    internal int ActiveDistance { get; set; }
    internal int IdleDistance { get; set; }
    internal int FreeHandTakeovers { get; set; }
    internal int FastJumpViolations { get; set; }
    internal IReadOnlyList<HandAssignment> Assignments { get; set; } =
        Array.Empty<HandAssignment>();
}

internal sealed class SweepBurstWindow
{
    internal double Value { get; set; }
    internal double BaseDensity { get; set; }
    internal double MotionDensity { get; set; }
    internal double RawMotionDensity { get; set; }
    internal double Start { get; set; }
    internal double End { get; set; }
}

internal sealed class SweepBurstResult
{
    internal double Value { get; set; }
    internal double BaseDensity { get; set; }
    internal double MotionDensity { get; set; }
    internal double RawMotionDensity { get; set; }
    internal double StrongestWindowStart { get; set; }
    internal double StrongestWindowEnd { get; set; }
    internal IReadOnlyList<SweepBurstWindow> Windows { get; set; } =
        Array.Empty<SweepBurstWindow>();
}
