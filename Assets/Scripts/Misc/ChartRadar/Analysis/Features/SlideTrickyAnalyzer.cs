using System;
using System.Collections.Generic;
using System.Linq;
using SimaiRadar.Core;

#nullable enable

namespace SimaiRadar.Analysis.Features;

internal sealed class SlideTrickyAnalyzer : IRadarFeatureAnalyzer
{
    private const double SimultaneousSeconds = 1.0 / 60;
    private const int ObjectCap = 16;
    private const int TopCount = 5;
    private const double WaitBucketSeconds = 0.05;
    private const double MaxWaitModeRatio = 4;
    private const double SpeedReferenceEighthBpm = 180;
    private const double SpeedExponent = 0.5;
    private const double SpeedMaxFactor = 1.5;
    private const double TouchWeight = 1.5;
    private const int TouchCap = 2;
    private const double SamePositionMultiplier = 1.5;
    private const double PendingHeadMultiplier = 2;
    private const double MultiSlideUplift = 0.15;
    private const double Tolerance = 1e-9;
    private static readonly double[] TopWeights = Enumerable.Range(1, TopCount)
        .Select(rank => 1 / Math.Log(rank + 1, 2)).ToArray();

    private sealed class Group
    {
        internal int Key { get; set; }
        internal IReadOnlyList<RadarEvent> Events { get; set; } = Array.Empty<RadarEvent>();
        internal double DeclarationTime { get; set; }
        internal BeatPosition DeclarationBeat { get; set; }
        internal double LaunchTime { get; set; }
        internal IReadOnlyList<double> LaunchTimes { get; set; } = Array.Empty<double>();
        internal IReadOnlyList<(double Start, double End)> ActiveIntervals { get; set; } =
            Array.Empty<(double, double)>();
        internal int? HeadEventId { get; set; }
    }

    private sealed class Cluster
    {
        internal double DeclarationTime { get; set; }
        internal BeatPosition DeclarationBeat { get; set; }
        internal IReadOnlyList<Group> Groups { get; set; } = Array.Empty<Group>();
    }

    private sealed class Point
    {
        internal int Id { get; set; }
        internal double Time { get; set; }
        internal double Weight { get; set; }
        internal string Kind { get; set; } = string.Empty;
        internal BeatPosition? Beat { get; set; }
        internal string? Position { get; set; }
        internal int? OwnerGroup { get; set; }
        internal double HeadBodyWeight { get; set; }
    }

    private readonly struct Assigned
    {
        internal Assigned(Point point, string phase) { Point = point; Phase = phase; }
        internal Point Point { get; }
        internal string Phase { get; }
    }

    private readonly struct TrickyValue
    {
        internal TrickyValue(double load) => Load = load;
        internal double Load { get; }
    }

    private readonly struct TempoPoint
    {
        internal TempoPoint(double time, BeatPosition beat, double bpm)
        { Time = time; Beat = beat; Bpm = bpm; }
        internal double Time { get; }
        internal BeatPosition Beat { get; }
        internal double Bpm { get; }
    }

    public RadarFeatureResult Analyze(AnalysisContext context)
    {
        if (context.DurationSeconds <= 0) return RadarFeatureResult.Failure("Chart duration must be positive.");
        var groups = Groups(context.Events);
        if (groups.Count == 0) return RadarFeatureResult.Success(0);
        var clusters = Clusters(groups);
        var points = Points(context.Events, groups);
        var assigned = Assign(clusters, points);
        var modeWait = ModalWait(groups);
        var loads = new List<double>();
        for (var index = 0; index < clusters.Count; index++)
        {
            var filtered = Filter(clusters[index], modeWait);
            if (filtered is null) continue;
            var clusterPoints = assigned[index];
            if (!ReferenceEquals(filtered, clusters[index]))
                clusterPoints = Assign(new[] { filtered }, clusterPoints.Select(item => item.Point).ToArray())[0];
            loads.Add(ClusterTricky(filtered, clusterPoints).Load);
        }
        var top = loads.OrderByDescending(value => value).Take(TopCount).ToArray();
        return RadarFeatureResult.Success(
            top.Select((value, index) => value * TopWeights[index]).Sum() / TopWeights.Sum());
    }

    private static List<Group> Groups(IReadOnlyList<RadarEvent> events)
    {
        var tempo = events.Where(item => item.Kind == RadarEventKind.Timing && item.Bpm is not null)
            .Select(item => new TempoPoint(item.StartTimeSeconds, item.StartBeat, item.Bpm!.Value))
            .OrderBy(item => item.Beat).ToArray();
        var output = new List<Group>();
        foreach (var grouping in events.Where(item =>
                         item.Kind == RadarEventKind.Slide && item.SlideGroupId is not null)
                     .GroupBy(item => item.SlideGroupId!.Value))
        {
            var paths = grouping.ToArray();
            var valid = paths.Where(item => item.EndTimeSeconds > item.StartTimeSeconds).ToArray();
            if (valid.Length == 0) continue;
            var times = paths.Select(item => item.SlideDeclareTimeSeconds).Distinct().ToArray();
            var beats = paths.Select(item => item.SlideDeclareBeat).Distinct().ToArray();
            var headIds = paths.Select(item => item.HeadEventId).Distinct().ToArray();
            if (times.Length != 1 || times[0] is null || beats.Length != 1 || beats[0] is null ||
                headIds.Length != 1)
                throw new InvalidOperationException("Slide group declaration metadata is inconsistent.");
            output.Add(new Group
            {
                Key = grouping.Key,
                Events = valid,
                DeclarationTime = times[0]!.Value,
                DeclarationBeat = beats[0]!.Value,
                LaunchTime = valid.Max(item => item.StartTimeSeconds),
                LaunchTimes = valid.Select(item => item.StartTimeSeconds).Distinct().OrderBy(value => value).ToArray(),
                ActiveIntervals = valid.Select(item =>
                {
                    var oneBeatEnd = SecondsAtBeat(item.StartBeat + new BeatPosition(1), tempo);
                    return (item.StartTimeSeconds, Math.Min(item.EndTimeSeconds, oneBeatEnd));
                }).ToArray(),
                HeadEventId = headIds[0]
            });
        }
        return output.OrderBy(item => item.DeclarationBeat)
            .ThenBy(item => item.DeclarationTime).ThenBy(item => item.Key).ToList();
    }

    private static double SecondsAtBeat(BeatPosition beat, IReadOnlyList<TempoPoint> tempo)
    {
        for (var index = tempo.Count - 1; index >= 0; index--)
        {
            if (tempo[index].Beat <= beat)
                return tempo[index].Time + (beat - tempo[index].Beat).ToDouble() * 60 / tempo[index].Bpm;
        }
        throw new InvalidOperationException("Beat precedes the first BPM event.");
    }

    private static List<Cluster> Clusters(IReadOnlyList<Group> groups)
    {
        var buckets = new List<List<Group>>();
        foreach (var group in groups)
        {
            if (buckets.Count == 0 ||
                group.DeclarationTime - buckets[^1][0].DeclarationTime > SimultaneousSeconds + Tolerance)
                buckets.Add(new List<Group>());
            buckets[^1].Add(group);
        }
        return buckets.Select(bucket => new Cluster
        {
            DeclarationTime = bucket[0].DeclarationTime,
            DeclarationBeat = bucket[0].DeclarationBeat,
            Groups = bucket
        }).ToList();
    }

    private static List<Point> Points(IReadOnlyList<RadarEvent> events, IReadOnlyList<Group> groups)
    {
        var headOwners = groups.Where(item => item.HeadEventId is not null)
            .ToDictionary(item => item.HeadEventId!.Value, item => item.Key);
        var headWeights = groups.Where(item => item.HeadEventId is not null)
            .ToDictionary(item => item.HeadEventId!.Value, item => (double)item.Events.Count);
        var output = new List<Point>();
        var id = 0;
        foreach (var item in events.Where(item => item.Kind is RadarEventKind.Tap or RadarEventKind.Hold))
            output.Add(new Point
            {
                Id = ++id, Time = item.StartTimeSeconds, Weight = 1, Kind = "button",
                Beat = item.StartBeat, Position = item.Position,
                OwnerGroup = headOwners.TryGetValue(item.EventId, out var owner) ? owner : null,
                HeadBodyWeight = headWeights.TryGetValue(item.EventId, out var weight) ? weight : 0
            });
        var touches = events.Where(item => item.Kind is RadarEventKind.Touch or RadarEventKind.TouchHold).ToArray();
        foreach (var touch in Workload.SimultaneousTouchPoints(touches, TouchWeight))
            output.Add(new Point { Id = ++id, Time = touch.TimeSeconds, Weight = touch.Weight, Kind = "touch" });
        foreach (var group in groups)
        {
            foreach (var launch in group.Events.GroupBy(item => item.StartTimeSeconds).OrderBy(item => item.Key))
                output.Add(new Point
                {
                    Id = ++id, Time = launch.Key, Weight = launch.Count(), Kind = "slide_launch",
                    OwnerGroup = group.Key
                });
        }
        return output.OrderBy(item => item.Time).ThenBy(item => item.Id).ToList();
    }

    private static List<List<Assigned>> Assign(IReadOnlyList<Cluster> clusters, IReadOnlyList<Point> points)
    {
        var assigned = Enumerable.Range(0, clusters.Count).Select(_ => new List<Assigned>()).ToList();
        foreach (var point in points)
        {
            var candidates = new List<(int Priority, double Distance, int Index, string Phase)>();
            for (var index = 0; index < clusters.Count; index++)
            {
                var cluster = clusters[index];
                var keys = cluster.Groups.Select(item => item.Key).ToHashSet();
                if (point.OwnerGroup is int owner && keys.Contains(owner)) continue;
                var launch = cluster.Groups.Any(group => group.LaunchTimes.Any(time => SameTime(point.Time, time)));
                var waiting = cluster.Groups.Where(group =>
                    point.Time >= group.DeclarationTime - Tolerance &&
                    point.Time < group.LaunchTime - Tolerance).ToArray();
                var active = cluster.Groups.SelectMany(group => group.ActiveIntervals)
                    .Where(interval => point.Time > interval.Start + Tolerance &&
                                       point.Time <= interval.End + Tolerance).ToArray();
                if (launch) candidates.Add((0, 0, index, "launch"));
                else if (waiting.Length > 0)
                    candidates.Add((1, waiting.Min(group => group.LaunchTime - point.Time), index, "waiting"));
                else if (active.Length > 0)
                    candidates.Add((2, active.Min(interval => point.Time - interval.Start), index, "active"));
            }
            if (candidates.Count > 0)
            {
                var selected = candidates.OrderBy(item => item.Priority).ThenBy(item => item.Distance)
                    .ThenBy(item => item.Index).First();
                assigned[selected.Index].Add(new Assigned(point, selected.Phase));
            }
        }
        return assigned;
    }

    private static TrickyValue ClusterTricky(Cluster cluster, IReadOnlyList<Assigned> assigned)
    {
        var targets = cluster.Groups.SelectMany(group => group.Events).Select(item => item.Position!)
            .ToHashSet();
        var touchIds = assigned.Where(item => item.Point.Kind == "touch")
            .OrderBy(item => item.Point.Time).ThenBy(item => item.Point.Id)
            .Take(TouchCap).Select(item => item.Point.Id).ToHashSet();
        var counted = assigned.Where(item => item.Point.Kind != "touch" || touchIds.Contains(item.Point.Id)).ToArray();
        var launch = counted.Where(item => item.Phase == "launch").Select(item => item.Point).ToArray();
        var internalItems = counted.Where(item => item.Phase != "launch").ToArray();
        var internalButtons = internalItems.Where(item => item.Point.Kind == "button").Select(item => item.Point).ToArray();
        var internalOther = internalItems.Where(item => item.Point.Kind != "button").Select(item => item.Point).ToArray();
        var waitingIds = internalItems.Where(item => item.Phase == "waiting" && item.Point.Kind == "button")
            .Select(item => item.Point.Id).ToHashSet();
        var speed = OrdinarySpeed(cluster, counted);
        var internalLoad = SweepAdjustedButtons(internalButtons, targets, waitingIds, speed) +
                           internalOther.Sum(item => item.Weight);
        var headOwners = launch.Where(item => item.Kind == "button" && item.HeadBodyWeight > 0)
            .Select(item => item.OwnerGroup).ToHashSet();
        var deduplicatedLaunch = launch.Where(item =>
            !(item.Kind == "slide_launch" && headOwners.Contains(item.OwnerGroup))).ToArray();
        var launchLoad = deduplicatedLaunch.Sum(item => item.Weight + item.HeadBodyWeight) / 2;
        var logicalCount = internalOther.Sum(item => item.Kind == "slide_launch" ? (int)item.Weight : 1) +
                           internalButtons.Length + deduplicatedLaunch.Sum(item =>
                               item.Kind == "slide_launch" ? (int)item.Weight :
                               item.Kind == "button" ? 1 + (int)item.HeadBodyWeight : 1);
        var capFactor = logicalCount > 0 ? Math.Min(1, (double)ObjectCap / logicalCount) : 1;
        var concurrency = cluster.Groups.Sum(group => Math.Sqrt(group.Events.Count));
        var multiplier = 1 + MultiSlideUplift * Math.Max(0, concurrency - 1);
        return new TrickyValue((internalLoad + launchLoad) * capFactor * multiplier);
    }

    private static double SweepAdjustedButtons(
        IReadOnlyList<Point> points,
        HashSet<string> targets,
        HashSet<int> samePositionIds,
        double speed)
    {
        if (points.Count == 0) return 0;
        var batches = points.GroupBy(item => item.Beat!.Value).OrderBy(item => item.Key);
        var runLength = 0;
        Dictionary<int, int>? previousPositions = null;
        BeatPosition? previousBeat = null, interval = null;
        int? direction = null;
        var total = 0.0;
        foreach (var batchGroup in batches)
        {
            var batch = batchGroup.ToArray();
            var batchWeight = batch.Sum(point => ButtonWeight(point, targets, samePositionIds, speed));
            if (batch.Length is not (1 or 2))
            {
                total += batchWeight; runLength = 0; previousPositions = null;
                previousBeat = interval = null; direction = null; continue;
            }
            var positions = batch.GroupBy(item => int.Parse(item.Position!))
                .ToDictionary(item => item.Key, item => item.Count());
            BeatPosition? currentInterval = previousBeat is null
                ? null
                : batchGroup.Key - previousBeat.Value;
            var currentDirection = previousPositions is null || previousPositions.Values.Sum() != batch.Length
                ? null : AdjacentDirection(previousPositions, positions);
            var continues = previousBeat is not null && currentDirection is not null &&
                currentInterval > BeatPosition.Zero &&
                (runLength == 1 || currentDirection == direction && currentInterval == interval);
            if (continues)
            {
                runLength++;
                if (runLength == 2) { direction = currentDirection; interval = currentInterval; }
            }
            else if (previousBeat is not null && currentDirection is not null && currentInterval > BeatPosition.Zero)
            { runLength = 2; direction = currentDirection; interval = currentInterval; }
            else { runLength = 1; direction = null; interval = null; }
            total += batchWeight / Math.Log(Math.Max(2, runLength - 1), 2);
            previousPositions = positions; previousBeat = batchGroup.Key;
        }
        return total;
    }

    private static double ButtonWeight(Point point, HashSet<string> targets, HashSet<int> eligible, double speed)
    {
        var multiplier = eligible.Contains(point.Id) && targets.Contains(point.Position!)
            ? SamePositionMultiplier : 1;
        if (point.HeadBodyWeight > 0) multiplier = Math.Max(multiplier, PendingHeadMultiplier);
        return point.Weight * multiplier * (point.HeadBodyWeight == 0 ? speed : 1);
    }

    private static int? AdjacentDirection(Dictionary<int, int> left, Dictionary<int, int> right)
    {
        bool EqualShift(int delta)
        {
            var shifted = left.ToDictionary(pair => (pair.Key - 1 + delta + 8) % 8 + 1, pair => pair.Value);
            return shifted.Count == right.Count && shifted.All(pair =>
                right.TryGetValue(pair.Key, out var value) && value == pair.Value);
        }
        if (EqualShift(1)) return 1;
        if (EqualShift(-1)) return -1;
        return null;
    }

    private static double OrdinarySpeed(Cluster cluster, IReadOnlyList<Assigned> assigned)
    {
        var times = assigned.Where(item => item.Point.Kind == "button" && item.Point.HeadBodyWeight == 0)
            .Select(item => item.Point.Time).Distinct().OrderBy(value => value).ToArray();
        if (times.Length == 0) return 1;
        var timeline = new[] { cluster.DeclarationTime }.Concat(times).ToArray();
        var gaps = timeline.Zip(timeline.Skip(1), (left, right) => right - left)
            .Where(gap => gap > Tolerance).OrderBy(value => value).ToArray();
        if (gaps.Length == 0) return 1;
        var median = gaps.Length % 2 == 1 ? gaps[gaps.Length / 2] :
            (gaps[gaps.Length / 2 - 1] + gaps[gaps.Length / 2]) / 2;
        var ratio = (30 / median) / SpeedReferenceEighthBpm;
        return ratio < 1 ? Math.Pow(ratio, SpeedExponent) : Math.Min(SpeedMaxFactor, ratio);
    }

    private static double? ModalWait(IReadOnlyList<Group> groups)
    {
        var buckets = new Dictionary<int, List<double>>();
        foreach (var group in groups)
            foreach (var path in group.Events)
            {
                var wait = path.StartTimeSeconds - group.DeclarationTime;
                if (wait <= Tolerance) continue;
                var bucket = (int)Math.Floor((wait + Tolerance) / WaitBucketSeconds + 0.5);
                if (!buckets.TryGetValue(bucket, out var values)) buckets[bucket] = values = new List<double>();
                values.Add(wait);
            }
        if (buckets.Count == 0) return null;
        var winner = buckets.OrderByDescending(item => item.Value.Count).ThenByDescending(item => item.Key).First().Value;
        winner.Sort();
        return winner.Count % 2 == 1 ? winner[winner.Count / 2] :
            (winner[winner.Count / 2 - 1] + winner[winner.Count / 2]) / 2;
    }

    private static Cluster? Filter(Cluster cluster, double? modeWait)
    {
        if (modeWait is null) return cluster;
        var threshold = modeWait.Value * MaxWaitModeRatio;
        var groups = new List<Group>();
        var changed = false;
        foreach (var group in cluster.Groups)
        {
            var keptIndexes = Enumerable.Range(0, group.Events.Count).Where(index =>
            {
                var wait = group.Events[index].StartTimeSeconds - group.DeclarationTime;
                var remove = wait > threshold && Math.Abs(wait - threshold) > Tolerance;
                changed |= remove;
                return !remove;
            }).ToArray();
            if (keptIndexes.Length == group.Events.Count) groups.Add(group);
            else if (keptIndexes.Length > 0)
            {
                var events = keptIndexes.Select(index => group.Events[index]).ToArray();
                groups.Add(new Group
                {
                    Key = group.Key, Events = events, DeclarationTime = group.DeclarationTime,
                    DeclarationBeat = group.DeclarationBeat, HeadEventId = group.HeadEventId,
                    LaunchTime = events.Max(item => item.StartTimeSeconds),
                    LaunchTimes = events.Select(item => item.StartTimeSeconds).Distinct().OrderBy(value => value).ToArray(),
                    ActiveIntervals = keptIndexes.Select(index => group.ActiveIntervals[index]).ToArray()
                });
            }
        }
        if (!changed) return cluster;
        return groups.Count == 0 ? null : new Cluster
        {
            DeclarationTime = cluster.DeclarationTime,
            DeclarationBeat = cluster.DeclarationBeat,
            Groups = groups
        };
    }

    private static bool SameTime(double left, double right) => Math.Abs(left - right) <= Tolerance;
}
