using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace SimaiRadar.Analysis.Features;

/// <summary>
/// Connects recognized groups into chronological families. Only the constants
/// used by the active Sweep model live here; experimental Python modes are not
/// part of the runtime surface.
/// </summary>
internal static class SweepFamilyBuilder
{
    private const double SingleConnectionIncrement = 0.2;
    private const double DoubleConnectionIncrement = 0.0;
    private const double SpeedChangeIncrement = 0.2;
    private const double SameDirectionIncrement = 0.1;
    private const double ReversalIncrement = 0.2;
    private const double Tolerance = 1e-9;

    internal static (IReadOnlyList<ScoredSweepGroup> Groups, IReadOnlyList<SweepFamily> Families)
        Build(IReadOnlyList<SweepSequence> sequences)
    {
        var ordered = sequences.OrderBy(item => item.StartTime).ThenBy(item => item.EndTime)
            .ThenBy(item => item, Comparer<SweepSequence>.Create(CompareLanes)).ToArray();
        var multipliers = Enumerable.Repeat(1.0, ordered.Length).ToArray();
        var parents = new int?[ordered.Length];
        for (var childIndex = 0; childIndex < ordered.Length; childIndex++)
        {
            (double Multiplier, double Nearness, int Earlier, int Parent)? best = null;
            for (var parentIndex = 0; parentIndex < childIndex; parentIndex++)
            {
                var increment = ConnectionIncrement(ordered[parentIndex], ordered[childIndex]);
                if (increment is null) continue;
                var candidate = (
                    multipliers[parentIndex] + increment.Value,
                    -Math.Abs(ordered[childIndex].StartTime - ordered[parentIndex].EndTime),
                    -parentIndex,
                    parentIndex);
                if (best is null || CompareChoice(candidate, best.Value) > 0) best = candidate;
            }
            if (best is null) continue;
            multipliers[childIndex] = best.Value.Multiplier;
            parents[childIndex] = best.Value.Parent;
        }

        var groups = ordered.Select((sequence, index) => new ScoredSweepGroup
        {
            Id = index + 1,
            Sequence = sequence,
            ParentId = parents[index] is null ? null : parents[index] + 1
        }).ToArray();
        var byRoot = new SortedDictionary<int, List<int>>();
        for (var index = 0; index < groups.Length; index++)
        {
            var root = index;
            while (parents[root] is not null) root = parents[root]!.Value;
            if (!byRoot.TryGetValue(root, out var members)) byRoot[root] = members = new List<int>();
            members.Add(groups[index].Id);
        }
        var families = byRoot.Select((pair, index) => new SweepFamily
        {
            Id = index + 1,
            GroupIds = pair.Value
        }).ToArray();
        return (groups, families);
    }

    internal static bool SameDirection(ScoredSweepGroup parent, ScoredSweepGroup child) =>
        parent.Sequence.Strands.Select(item => item.FinalDirection)
            .Intersect(child.Sequence.Strands.Select(item => item.InitialDirection)).Any();

    private static double? ConnectionIncrement(SweepSequence parent, SweepSequence child)
    {
        var gap = child.StartTime - parent.EndTime;
        if (gap < -Tolerance || gap > parent.MedianIntervalSeconds + Tolerance) return null;
        var doubleConnection = Math.Abs(gap) <= Tolerance;
        var speedChanged = !SameSpeed(parent.MedianIntervalSeconds, child.MedianIntervalSeconds);
        var parentDirections = parent.Strands.Select(item => item.FinalDirection).ToHashSet();
        var childDirections = child.Strands.Select(item => item.InitialDirection).ToHashSet();
        var nearStarts = parent.LanesByBatch[0].Any(left =>
            child.LanesByBatch[0].Any(right => SweepRecognizer.CircularDistance(left, right) <= 1));
        var sameDirection = parentDirections.Overlaps(childDirections);
        var fold = parentDirections.Any(left => childDirections.Contains(-left)) &&
            parent.LanesByBatch[^1].Any(left => child.LanesByBatch[0]
                .Any(right => SweepRecognizer.CircularDistance(left, right) <= 1));
        var sameDirectionBonus = sameDirection && nearStarts;
        var increment = doubleConnection ? DoubleConnectionIncrement :
            sameDirectionBonus || fold ? SingleConnectionIncrement : 0;
        if (speedChanged) increment += SpeedChangeIncrement;
        if (sameDirectionBonus) increment += SameDirectionIncrement;
        else if (fold) increment += ReversalIncrement;
        return increment;
    }

    private static bool SameSpeed(double left, double right) =>
        Math.Abs(left - right) <= Math.Max(Tolerance,
            SweepRecognizer.SpeedRelativeTolerance * Math.Max(Math.Abs(left), Math.Abs(right)));

    private static int CompareChoice(
        (double Multiplier, double Nearness, int Earlier, int Parent) left,
        (double Multiplier, double Nearness, int Earlier, int Parent) right)
    {
        var result = left.Multiplier.CompareTo(right.Multiplier);
        if (result != 0) return result;
        result = left.Nearness.CompareTo(right.Nearness);
        return result != 0 ? result : left.Earlier.CompareTo(right.Earlier);
    }

    private static int CompareLanes(SweepSequence left, SweepSequence right)
    {
        for (var batch = 0; batch < Math.Min(left.LanesByBatch.Count, right.LanesByBatch.Count); batch++)
        {
            var a = left.LanesByBatch[batch];
            var b = right.LanesByBatch[batch];
            for (var lane = 0; lane < Math.Min(a.Count, b.Count); lane++)
            {
                var result = a[lane].CompareTo(b[lane]);
                if (result != 0) return result;
            }
            var length = a.Count.CompareTo(b.Count);
            if (length != 0) return length;
        }
        return left.LanesByBatch.Count.CompareTo(right.LanesByBatch.Count);
    }
}
