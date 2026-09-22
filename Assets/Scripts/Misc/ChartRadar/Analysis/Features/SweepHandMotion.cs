using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

#nullable enable

namespace SimaiRadar.Analysis.Features;

/// <summary>Two-hand displacement DP used by Sweep burst motion scoring.</summary>
internal static class SweepHandMotion
{
    private const double ReferenceStepSeconds = 1.0 / 12;

    private readonly struct MotionState : IEquatable<MotionState>
    {
        internal MotionState(int? left, int? right, int? leftBatch, int? rightBatch)
        {
            Left = left; Right = right; LeftBatch = leftBatch; RightBatch = rightBatch;
        }
        internal int? Left { get; }
        internal int? Right { get; }
        internal int? LeftBatch { get; }
        internal int? RightBatch { get; }
        public bool Equals(MotionState other) => Left == other.Left && Right == other.Right &&
            LeftBatch == other.LeftBatch && RightBatch == other.RightBatch;
        public override bool Equals(object? obj) => obj is MotionState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Left, Right, LeftBatch, RightBatch);
    }

    private sealed class MotionRecord
    {
        internal int FastJumps { get; set; }
        internal int TotalDistance { get; set; }
        internal int ActiveDistance { get; set; }
        internal int IdleDistance { get; set; }
        internal int Takeovers { get; set; }
        internal IReadOnlyList<HandAssignment> Assignments { get; set; } =
            Array.Empty<HandAssignment>();
    }

    private sealed class AssignmentOption
    {
        internal IReadOnlyList<int> LeftLanes { get; set; } = Array.Empty<int>();
        internal IReadOnlyList<int> RightLanes { get; set; } = Array.Empty<int>();
        internal int? LeftTarget { get; set; }
        internal int? RightTarget { get; set; }
    }

    private readonly struct MoveCost
    {
        internal MoveCost(int distance, int active, int idle, int fast)
        { Distance = distance; Active = active; Idle = idle; Fast = fast; }
        internal int Distance { get; }
        internal int Active { get; }
        internal int Idle { get; }
        internal int Fast { get; }
    }

    internal static HandMotionResult ForFamily(
        SweepFamily family,
        IReadOnlyList<ScoredSweepGroup> groups,
        CancellationToken cancellationToken = default)
    {
        var byId = groups.ToDictionary(item => item.Id);
        var batches = new SortedDictionary<double, HashSet<int>>();
        foreach (var groupId in family.GroupIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var group = byId[groupId];
            for (var index = 0; index < group.Sequence.Times.Count; index++)
            {
                var time = group.Sequence.Times[index];
                if (!batches.TryGetValue(time, out var lanes))
                    batches[time] = lanes = new HashSet<int>();
                lanes.UnionWith(group.Sequence.LanesByBatch[index]);
            }
        }
        return Calculate(
            batches.Keys.ToArray(),
            batches.Values.Select(lanes => (IReadOnlyList<int>)lanes.OrderBy(x => x).ToArray()).ToArray(),
            cancellationToken: cancellationToken);
    }

    internal static HandMotionResult Calculate(
        IReadOnlyList<double> times,
        IReadOnlyList<IReadOnlyList<int>> lanesByBatch,
        IReadOnlyCollection<int>? idleTransitionIndexes = null,
        CancellationToken cancellationToken = default)
    {
        if (times.Count == 0 || times.Count != lanesByBatch.Count)
            throw new InvalidOperationException(
                "Sweep hand-motion times and lane batches must have the same positive length.");
        var lanes = lanesByBatch.Select(batch =>
            (IReadOnlyList<int>)batch.Distinct().OrderBy(x => x).ToArray()).ToArray();
        var idle = idleTransitionIndexes is null
            ? new HashSet<int>() : idleTransitionIndexes.ToHashSet();
        if (times.Select((time, index) => (time, index)).Any(item =>
                double.IsNaN(item.time) || double.IsInfinity(item.time) ||
                item.index > 0 && item.time <= times[item.index - 1]))
            throw new InvalidOperationException("Sweep hand-motion times must be finite and increasing.");
        if (lanes.Any(batch => batch.Count is < 1 or > 3 || batch.Any(x => x is < 1 or > 8)))
            throw new InvalidOperationException("A Sweep hand-motion batch needs one to three outer lanes.");

        var states = new Dictionary<MotionState, MotionRecord>
        {
            [new MotionState(null, null, null, null)] = new MotionRecord()
        };
        for (var batchIndex = 0; batchIndex < times.Count; batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = new Dictionary<MotionState, MotionRecord>();
            foreach (var pair in states)
                foreach (var option in Options(lanes[batchIndex]))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var previousLeftUsed = pair.Key.LeftBatch == batchIndex - 1;
                    var previousRightUsed = pair.Key.RightBatch == batchIndex - 1;
                    var left = Cost(pair.Key.Left, option.LeftTarget, pair.Key.LeftBatch,
                        batchIndex, times, idle.Contains(batchIndex));
                    var right = Cost(pair.Key.Right, option.RightTarget, pair.Key.RightBatch,
                        batchIndex, times, idle.Contains(batchIndex));
                    var usedLeft = option.LeftTarget is not null;
                    var usedRight = option.RightTarget is not null;
                    var takeover = lanes[batchIndex].Count == 1 &&
                        (usedLeft && !previousLeftUsed && previousRightUsed ||
                         usedRight && !previousRightUsed && previousLeftUsed) ? 1 : 0;
                    var state = new MotionState(
                        usedLeft ? option.LeftTarget : pair.Key.Left,
                        usedRight ? option.RightTarget : pair.Key.Right,
                        usedLeft ? batchIndex : pair.Key.LeftBatch,
                        usedRight ? batchIndex : pair.Key.RightBatch);
                    var assignment = new HandAssignment
                    {
                        Time = times[batchIndex],
                        LeftLanes = option.LeftLanes,
                        RightLanes = option.RightLanes,
                        LeftPosition = state.Left,
                        RightPosition = state.Right,
                        LeftIdleDistance = left.Idle,
                        RightIdleDistance = right.Idle,
                        FreeHandTakeover = takeover,
                        FastJumpViolations = left.Fast + right.Fast
                    };
                    var record = new MotionRecord
                    {
                        FastJumps = pair.Value.FastJumps + left.Fast + right.Fast,
                        TotalDistance = pair.Value.TotalDistance + left.Distance + right.Distance,
                        ActiveDistance = pair.Value.ActiveDistance + left.Active + right.Active,
                        IdleDistance = pair.Value.IdleDistance + left.Idle + right.Idle,
                        Takeovers = pair.Value.Takeovers + takeover,
                        Assignments = pair.Value.Assignments.Concat(new[] { assignment }).ToArray()
                    };
                    if (!next.TryGetValue(state, out var previous) || Compare(record, previous) < 0)
                        next[state] = record;
                }
            states = next;
        }
        var best = states.Values.Aggregate((left, right) => Compare(left, right) <= 0 ? left : right);
        return new HandMotionResult
        {
            TotalDistance = best.TotalDistance,
            ActiveDistance = best.ActiveDistance,
            IdleDistance = best.IdleDistance,
            FreeHandTakeovers = best.Takeovers,
            FastJumpViolations = best.FastJumps,
            Assignments = best.Assignments
        };
    }

    private static IReadOnlyList<AssignmentOption> Options(IReadOnlyList<int> lanes)
    {
        if (lanes.Count == 1)
        {
            var lane = lanes[0];
            return new[]
            {
                Option(new[] { lane }, Array.Empty<int>(), lane, null),
                Option(Array.Empty<int>(), new[] { lane }, null, lane)
            };
        }
        if (lanes.Count == 2)
        {
            return new[]
            {
                Option(new[] { lanes[0] }, new[] { lanes[1] }, lanes[0], lanes[1]),
                Option(new[] { lanes[1] }, new[] { lanes[0] }, lanes[1], lanes[0])
            };
        }
        var output = new List<AssignmentOption>();
        for (var singleIndex = 0; singleIndex < 3; singleIndex++)
        {
            var single = lanes[singleIndex];
            var pair = lanes.Where((_, index) => index != singleIndex).ToArray();
            if (SweepRecognizer.CircularDistance(pair[0], pair[1]) != 1) continue;
            foreach (var pairPosition in pair)
            {
                output.Add(Option(pair, new[] { single }, pairPosition, single));
                output.Add(Option(new[] { single }, pair, single, pairPosition));
            }
        }
        return output;
    }

    private static AssignmentOption Option(
        IReadOnlyList<int> left, IReadOnlyList<int> right, int? leftTarget, int? rightTarget) =>
        new() { LeftLanes = left, RightLanes = right, LeftTarget = leftTarget, RightTarget = rightTarget };

    private static MoveCost Cost(
        int? previous, int? target, int? lastBatch, int batchIndex,
        IReadOnlyList<double> times, bool forceIdle)
    {
        if (target is null || previous is null) return new MoveCost(0, 0, 0, 0);
        var distance = SweepRecognizer.CircularDistance(previous.Value, target.Value);
        var active = lastBatch == batchIndex - 1 && !forceIdle;
        var fast = 0;
        if (active && batchIndex > 0)
        {
            var allowed = Math.Max(1, (int)Math.Floor(
                (times[batchIndex] - times[batchIndex - 1]) / ReferenceStepSeconds + 1e-9));
            fast = distance > allowed ? 1 : 0;
        }
        return new MoveCost(distance, active ? distance : 0, active ? 0 : distance, fast);
    }

    private static int Compare(MotionRecord left, MotionRecord right)
    {
        var values = new[]
        {
            left.FastJumps.CompareTo(right.FastJumps),
            left.Takeovers.CompareTo(right.Takeovers),
            left.TotalDistance.CompareTo(right.TotalDistance),
            left.ActiveDistance.CompareTo(right.ActiveDistance)
        };
        foreach (var value in values) if (value != 0) return value;
        for (var index = 0; index < Math.Min(left.Assignments.Count, right.Assignments.Count); index++)
        {
            var l = left.Assignments[index];
            var r = right.Assignments[index];
            var value = (l.LeftPosition ?? 0).CompareTo(r.LeftPosition ?? 0);
            if (value != 0) return value;
            value = (l.RightPosition ?? 0).CompareTo(r.RightPosition ?? 0);
            if (value != 0) return value;
        }
        return left.Assignments.Count.CompareTo(right.Assignments.Count);
    }
}
