using System;
using System.Collections.Generic;
using System.Linq;
using SimaiRadar.Core;

#nullable enable

namespace SimaiRadar.Analysis.Features;

/// <summary>
/// Scores the three strongest non-overlapping two-second Sweep windows. The
/// constants below are the active fitted-feature definition, not user options.
/// </summary>
internal sealed class SweepBurstAnalyzer : IRadarFeatureAnalyzer
{
    private const double WindowSeconds = 2;
    private const int WindowCount = 3;
    private const double WindowRankDecayExponent = 0.5;
    private const double ReferenceIntervalSeconds = 1.0 / 12;
    private const double SpeedExponent = 0.5;
    private const double ProtectedNoteWeight = 0.3;
    private const int SimpleRunFullAttacks = 16;
    private const double SimpleRunDecayExponent = 0.5;
    private const double SameDirectionConnectionBonus = 0.2;
    private const double SameDirectionHandoffBonus = 0.2;
    private const double EighthGapGroupBonus = 0.05;
    private const double IdleDistanceWeight = 0.5;
    private const double IdleSpeedReferenceKeysPerSecond = 10;
    private const double TakeoverWeight = 1;
    private const double FastJumpWeight = 2;
    private const double PatternMotionFloor = 0.1;
    private const int PatternMinimumGroups = 6;
    private const int PatternMaximumPeriod = 4;
    private const double PatternMinimumMatchRatio = 0.8;
    private const double Tolerance = 1e-9;
    private static readonly BeatPosition EighthBeat = new(1, 2);

    private readonly struct PatternToken : IEquatable<PatternToken>
    {
        internal PatternToken(char hand, int direction) { Hand = hand; Direction = direction; }
        internal char Hand { get; }
        internal int Direction { get; }
        public bool Equals(PatternToken other) => Hand == other.Hand && Direction == other.Direction;
        public override bool Equals(object? obj) => obj is PatternToken other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Hand, Direction);
    }

    private sealed class PointValue
    {
        internal double Base { get; set; }
        internal double Motion { get; set; }
        internal double RawMotion { get; set; }
    }

    public RadarFeatureResult Analyze(AnalysisContext context)
    {
        if (context.DurationSeconds <= 0)
            return RadarFeatureResult.Failure("Chart duration must be positive.");
        return RadarFeatureResult.Success(Score(context.Events, context.DurationSeconds).Value);
    }

    internal static SweepBurstResult Score(IReadOnlyList<RadarEvent> events, double durationSeconds)
    {
        var sequences = SweepRecognizer.Recognize(events);
        var (groups, families) = SweepFamilyBuilder.Build(sequences);
        var groupsById = groups.ToDictionary(item => item.Id);
        var motions = families.ToDictionary(
            family => family.Id, family => SweepHandMotion.ForFamily(family, groups));
        var eighthBonusGroups = EighthBonusGroups(groups);
        var points = new SortedDictionary<double, PointValue>();

        void AddPoint(double time, double baseValue = 0, double motion = 0, double rawMotion = 0)
        {
            if (!points.TryGetValue(time, out var point)) points[time] = point = new PointValue();
            point.Base += baseValue;
            point.Motion += motion;
            point.RawMotion += rawMotion;
        }

        var firstBatchPhysicalBase = new Dictionary<int, double>();
        foreach (var group in groups)
        {
            var sequence = group.Sequence;
            var speedByBatch = new[] { sequence.UnitTimeIntervals[0] }
                .Concat(sequence.UnitTimeIntervals).ToArray();
            var simpleRunLength = 0;
            for (var index = 0; index < sequence.Times.Count; index++)
            {
                var noteWeight = sequence.NormalDeclarations[index] +
                    sequence.ProtectedDeclarations[index] * ProtectedNoteWeight;
                var speedFactor = Math.Pow(
                    ReferenceIntervalSeconds / speedByBatch[index], SpeedExponent);
                if (index == 0)
                    firstBatchPhysicalBase[group.Id] = sequence.Widths[index] * speedFactor;

                var decay = 1.0;
                if (sequence.Widths[index] >= 2) simpleRunLength = 0;
                else
                {
                    if (sequence.SpeedSwitches.Contains(index) ||
                        sequence.DirectionSwitches.Contains(index)) simpleRunLength = 0;
                    simpleRunLength++;
                    if (simpleRunLength > SimpleRunFullAttacks)
                    {
                        var excessRank = simpleRunLength - SimpleRunFullAttacks + 1;
                        decay = Math.Pow(excessRank, -SimpleRunDecayExponent);
                    }
                }
                var baseValue = noteWeight * speedFactor * decay;
                if (eighthBonusGroups.Contains(group.Id))
                    baseValue += sequence.Widths[index] * speedFactor * EighthGapGroupBonus;
                if (sequence.DoubleHandoffs.Contains(index) &&
                    !sequence.DirectionSwitches.Contains(index))
                    baseValue += sequence.Widths[index] * speedFactor *
                        SameDirectionHandoffBonus;
                AddPoint(sequence.Times[index], baseValue);
            }
        }

        var sameDirectionGroups = new HashSet<int>();
        foreach (var group in groups)
        {
            if (group.ParentId is null) continue;
            var parent = groupsById[group.ParentId.Value];
            if (!SweepFamilyBuilder.SameDirection(parent, group)) continue;
            sameDirectionGroups.Add(group.Id);
            AddPoint(group.Sequence.StartTime,
                firstBatchPhysicalBase[group.Id] * SameDirectionConnectionBonus);
        }

        foreach (var family in families)
        {
            var motion = motions[family.Id];
            var regularStarts = RegularPatternGroups(
                    family, groupsById, motion, sameDirectionGroups)
                .Select(id => groupsById[id].Sequence.StartTime).ToHashSet();
            double? lastLeftUse = null, lastRightUse = null;
            foreach (var assignment in motion.Assignments)
            {
                var weightedIdle = 0.0;
                if (assignment.LeftIdleDistance != 0)
                {
                    if (lastLeftUse is null)
                        throw new InvalidOperationException("Idle displacement has no prior left-hand use.");
                    weightedIdle += IdlePressure(
                        assignment.LeftIdleDistance, assignment.Time - lastLeftUse.Value);
                }
                if (assignment.RightIdleDistance != 0)
                {
                    if (lastRightUse is null)
                        throw new InvalidOperationException("Idle displacement has no prior right-hand use.");
                    weightedIdle += IdlePressure(
                        assignment.RightIdleDistance, assignment.Time - lastRightUse.Value);
                }
                if (assignment.LeftLanes.Count != 0) lastLeftUse = assignment.Time;
                if (assignment.RightLanes.Count != 0) lastRightUse = assignment.Time;

                var idleBonus = weightedIdle * IdleDistanceWeight;
                var otherBonus = assignment.FreeHandTakeover * TakeoverWeight +
                    assignment.FastJumpViolations * FastJumpWeight;
                var rawBonus = idleBonus + otherBonus;
                if (rawBonus == 0) continue;
                var adjusted = regularStarts.Contains(assignment.Time)
                    ? rawBonus * PatternMotionFloor : rawBonus;
                AddPoint(assignment.Time, motion: adjusted, rawMotion: rawBonus);
            }
        }
        return Windows(points, durationSeconds);
    }

    private static HashSet<int> EighthBonusGroups(IReadOnlyList<ScoredSweepGroup> groups)
    {
        var output = new HashSet<int>();
        var endingAt = new Dictionary<BeatPosition, List<ScoredSweepGroup>>();
        foreach (var group in groups)
        {
            if (group.ParentId is null && endingAt.TryGetValue(
                    group.Sequence.StartBeat - EighthBeat, out var previous) &&
                previous.Any(parent => SameSpeed(
                    parent.Sequence.MedianIntervalSeconds,
                    group.Sequence.MedianIntervalSeconds, 0.1)))
                output.Add(group.Id);
            if (!endingAt.TryGetValue(group.Sequence.EndBeat, out var ending))
                endingAt[group.Sequence.EndBeat] = ending = new List<ScoredSweepGroup>();
            ending.Add(group);
        }
        return output;
    }

    private static HashSet<int> RegularPatternGroups(
        SweepFamily family,
        IReadOnlyDictionary<int, ScoredSweepGroup> groups,
        HandMotionResult motion,
        IReadOnlyCollection<int> protectedGroups)
    {
        var assignments = motion.Assignments.ToDictionary(item => item.Time);
        var output = new HashSet<int>();
        var segment = new List<(int GroupId, PatternToken Token)>();
        foreach (var group in family.GroupIds.Select(id => groups[id])
                     .OrderBy(item => item.Sequence.StartTime))
        {
            var token = SimpleToken(group, assignments);
            if (token is null)
            {
                output.UnionWith(MarkRegular(segment));
                segment.Clear();
            }
            else segment.Add((group.Id, token.Value));
        }
        output.UnionWith(MarkRegular(segment));
        output.ExceptWith(protectedGroups);
        return output;
    }

    private static PatternToken? SimpleToken(
        ScoredSweepGroup group, IReadOnlyDictionary<double, HandAssignment> assignments)
    {
        var sequence = group.Sequence;
        if (sequence.Widths.Any(width => width != 1) || sequence.SpeedSwitches.Count != 0 ||
            sequence.DirectionSwitches.Count != 0 || sequence.DoubleHandoffs.Count != 0)
            return null;
        var directions = sequence.Strands.SelectMany(strand =>
            new[] { strand.InitialDirection, strand.FinalDirection }).Distinct().ToArray();
        if (directions.Length != 1) return null;
        char? hand = null;
        foreach (var time in sequence.Times)
        {
            if (!assignments.TryGetValue(time, out var assignment)) return null;
            var current = assignment.LeftLanes.Count != 0 && assignment.RightLanes.Count == 0
                ? 'L' : assignment.RightLanes.Count != 0 && assignment.LeftLanes.Count == 0
                    ? 'R' : (char?)null;
            if (current is null || hand is not null && hand != current) return null;
            hand = current;
        }
        return new PatternToken(hand!.Value, directions[0]);
    }

    private static HashSet<int> MarkRegular(
        IReadOnlyList<(int GroupId, PatternToken Token)> segment)
    {
        if (segment.Count < PatternMinimumGroups) return new HashSet<int>();
        PatternToken[]? bestTemplate = null;
        var bestQuality = double.NegativeInfinity;
        var bestPeriod = int.MaxValue;
        for (var period = 1;
             period <= Math.Min(PatternMaximumPeriod, segment.Count / 3);
             period++)
        {
            var template = Enumerable.Range(0, period).Select(phase =>
                MostCommon(segment.Where((_, index) => index % period == phase)
                    .Select(item => item.Token))).ToArray();
            var matched = segment.Select((item, index) =>
                item.Token.Equals(template[index % period])).Count(value => value);
            var ratio = (double)matched / segment.Count;
            if (ratio < PatternMinimumMatchRatio) continue;
            var quality = ratio - 0.02 * (period - 1);
            if (quality > bestQuality || quality == bestQuality && period < bestPeriod)
            {
                bestQuality = quality;
                bestPeriod = period;
                bestTemplate = template;
            }
        }
        if (bestTemplate is null) return new HashSet<int>();
        return segment.Select((item, index) => (item, index))
            .Where(pair => pair.item.Token.Equals(bestTemplate[pair.index % bestTemplate.Length]))
            .Select(pair => pair.item.GroupId).ToHashSet();
    }

    private static PatternToken MostCommon(IEnumerable<PatternToken> values)
    {
        var counts = new Dictionary<PatternToken, (int Count, int First)>();
        var index = 0;
        foreach (var value in values)
        {
            if (counts.TryGetValue(value, out var row)) counts[value] = (row.Count + 1, row.First);
            else counts[value] = (1, index);
            index++;
        }
        return counts.OrderByDescending(pair => pair.Value.Count)
            .ThenBy(pair => pair.Value.First).First().Key;
    }

    private static double IdlePressure(int distance, double elapsed)
    {
        if (distance == 0) return 0;
        if (elapsed <= 0) throw new InvalidOperationException("Idle hand travel requires positive time.");
        var ratio = distance / (elapsed * IdleSpeedReferenceKeysPerSecond);
        return distance * Math.Sqrt(Math.Max(1, ratio));
    }

    private static SweepBurstResult Windows(
        SortedDictionary<double, PointValue> points, double duration)
    {
        var maxStart = Math.Max(0, duration - WindowSeconds);
        var starts = new HashSet<double> { 0, maxStart };
        foreach (var time in points.Keys)
        {
            starts.Add(Math.Min(maxStart, Math.Max(0, time)));
            starts.Add(Math.Min(maxStart, Math.Max(0, time - WindowSeconds)));
        }
        var times = points.Keys.ToArray();
        var basePrefix = Prefix(points.Values.Select(item => item.Base));
        var motionPrefix = Prefix(points.Values.Select(item => item.Motion));
        var rawPrefix = Prefix(points.Values.Select(item => item.RawMotion));
        var candidates = starts.OrderBy(value => value).Select(start =>
        {
            var left = LowerBound(times, start - Tolerance);
            var right = LowerBound(times, start + WindowSeconds - Tolerance);
            var baseDensity = (basePrefix[right] - basePrefix[left]) / WindowSeconds;
            var motionDensity = (motionPrefix[right] - motionPrefix[left]) / WindowSeconds;
            var rawDensity = (rawPrefix[right] - rawPrefix[left]) / WindowSeconds;
            return new SweepBurstWindow
            {
                Value = baseDensity + motionDensity,
                BaseDensity = baseDensity,
                MotionDensity = motionDensity,
                RawMotionDensity = rawDensity,
                Start = start,
                End = start + WindowSeconds
            };
        }).ToArray();

        var selected = new List<SweepBurstWindow>();
        foreach (var candidate in candidates.OrderByDescending(item => item.Value)
                     .ThenBy(item => item.Start))
        {
            if (candidate.Value <= 0 || selected.Any(other =>
                    candidate.Start < other.End - Tolerance &&
                    other.Start < candidate.End - Tolerance)) continue;
            selected.Add(candidate);
            if (selected.Count == WindowCount) break;
        }
        if (selected.Count == 0)
        {
            var empty = new SweepBurstWindow { Start = 0, End = WindowSeconds };
            return new SweepBurstResult
            {
                StrongestWindowStart = 0,
                StrongestWindowEnd = WindowSeconds,
                Windows = new[] { empty }
            };
        }

        var weights = Enumerable.Range(1, selected.Count)
            .Select(rank => Math.Pow(rank, -WindowRankDecayExponent)).ToArray();
        var weightTotal = weights.Sum();
        double Weighted(Func<SweepBurstWindow, double> property) =>
            selected.Select((window, index) => weights[index] * property(window)).Sum() / weightTotal;
        return new SweepBurstResult
        {
            Value = Weighted(item => item.Value),
            BaseDensity = Weighted(item => item.BaseDensity),
            MotionDensity = Weighted(item => item.MotionDensity),
            RawMotionDensity = Weighted(item => item.RawMotionDensity),
            StrongestWindowStart = selected[0].Start,
            StrongestWindowEnd = selected[0].End,
            Windows = selected
        };
    }

    private static double[] Prefix(IEnumerable<double> values)
    {
        var output = new List<double> { 0 };
        foreach (var value in values) output.Add(output[^1] + value);
        return output.ToArray();
    }

    private static int LowerBound(IReadOnlyList<double> values, double target)
    {
        var low = 0;
        var high = values.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (values[middle] < target) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private static bool SameSpeed(double left, double right, double relativeTolerance) =>
        Math.Abs(left - right) <= Math.Max(Tolerance,
            relativeTolerance * Math.Max(Math.Abs(left), Math.Abs(right)));
}
