using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace MajdataPlay.Utils.ChartRadar;

internal sealed class SlideCumulateAnalyzer : IRadarFeatureAnalyzer
{
    private const double Alpha = 0.35;
    private const double ReferenceSeconds = 0.5;
    private const double SimultaneousSeconds = 1.0 / 60;
    private const int EffectiveLengthBase = 5;
    private const double TouchWeight = 1.5;
    private const double Tolerance = 1e-9;

    private sealed class SlideGroup
    {
        internal int Key { get; set; }
        internal IReadOnlyList<RadarEvent> Events { get; set; } = Array.Empty<RadarEvent>();
        internal double DeclarationTime { get; set; }
        internal BeatPosition DeclarationBeat { get; set; }
        internal double LaunchTime { get; set; }
        internal int? HeadEventId { get; set; }
    }

    private sealed class Onset
    {
        internal double DeclarationTime { get; set; }
        internal BeatPosition DeclarationBeat { get; set; }
        internal IReadOnlyList<SlideGroup> Groups { get; set; } = Array.Empty<SlideGroup>();
    }

    private sealed class Point
    {
        internal string Id { get; set; } = string.Empty;
        internal double Time { get; set; }
        internal double Weight { get; set; }
        internal int? OwnerGroup { get; set; }
    }

    public RadarFeatureResult Analyze(AnalysisContext context)
    {
        context.ThrowIfCancellationRequested();
        var duration = context.DurationSeconds;
        if (duration <= 0) return RadarFeatureResult.Failure("Chart duration must be positive.");
        var groups = Groups(context.Events);
        if (groups.Count == 0) return RadarFeatureResult.Success(0);
        var onsets = Onsets(groups);
        var assigned = Assign(onsets, Points(context.Events, groups));
        var totalLoad = 0.0;
        foreach (var section in Sections(onsets))
        {
            context.ThrowIfCancellationRequested();
            var loads = section.Select(onset => OnsetLoad(onset, assigned[onsets.IndexOf(onset)])).ToArray();
            var mean = loads.Sum() / loads.Length;
            var rms = Math.Sqrt(loads.Sum(load => load * load) / loads.Length);
            var intensity = 0.8 * mean + 0.2 * rms;
            totalLoad += EffectiveLength(loads.Length) * intensity;
        }
        return RadarFeatureResult.Success(totalLoad / (duration / ReferenceSeconds));
    }

    private static List<SlideGroup> Groups(IReadOnlyList<RadarEvent> events)
    {
        var output = new List<SlideGroup>();
        foreach (var grouping in events.Where(item =>
                         item.Kind == RadarEventKind.Slide && item.SlideGroupId is not null)
                     .GroupBy(item => item.SlideGroupId!.Value))
        {
            var paths = grouping.ToArray();
            var declarationTimes = paths.Select(item => item.SlideDeclareTimeSeconds).Distinct().ToArray();
            var declarationBeats = paths.Select(item => item.SlideDeclareBeat).Distinct().ToArray();
            var headIds = paths.Select(item => item.HeadEventId).Distinct().ToArray();
            if (declarationTimes.Length != 1 || declarationTimes[0] is null ||
                declarationBeats.Length != 1 || declarationBeats[0] is null || headIds.Length != 1)
                throw new InvalidOperationException("Slide group declaration metadata is inconsistent.");
            var valid = paths.Where(item => item.EndTimeSeconds > item.StartTimeSeconds).ToArray();
            if (valid.Length == 0) continue;
            output.Add(new SlideGroup
            {
                Key = grouping.Key,
                Events = valid,
                DeclarationTime = declarationTimes[0]!.Value,
                DeclarationBeat = declarationBeats[0]!.Value,
                LaunchTime = valid.Max(item => item.StartTimeSeconds),
                HeadEventId = headIds[0]
            });
        }
        return output.OrderBy(item => item.DeclarationBeat)
            .ThenBy(item => item.DeclarationTime).ThenBy(item => item.Key).ToList();
    }

    private static List<Onset> Onsets(IReadOnlyList<SlideGroup> groups)
    {
        var buckets = new List<List<SlideGroup>>();
        foreach (var group in groups)
        {
            if (buckets.Count == 0 ||
                group.DeclarationTime - buckets[^1][0].DeclarationTime > SimultaneousSeconds + Tolerance)
                buckets.Add(new List<SlideGroup>());
            buckets[^1].Add(group);
        }
        return buckets.Select(bucket => new Onset
        {
            DeclarationTime = bucket[0].DeclarationTime,
            DeclarationBeat = bucket[0].DeclarationBeat,
            Groups = bucket
        }).ToList();
    }

    private static IReadOnlyList<IReadOnlyList<Onset>> Sections(IReadOnlyList<Onset> onsets)
    {
        var output = new List<IReadOnlyList<Onset>>();
        var current = new List<Onset> { onsets[0] };
        foreach (var onset in onsets.Skip(1))
        {
            var delta = (onset.DeclarationBeat - current[^1].DeclarationBeat).ToDouble();
            if (delta > Tolerance && delta <= 1 + Tolerance) current.Add(onset);
            else { output.Add(current); current = new List<Onset> { onset }; }
        }
        output.Add(current);
        return output;
    }

    private static List<Point> Points(IReadOnlyList<RadarEvent> events, IReadOnlyList<SlideGroup> groups)
    {
        var headOwners = groups.Where(item => item.HeadEventId is not null)
            .ToDictionary(item => item.HeadEventId!.Value, item => item.Key);
        var output = events.Where(item => item.Kind is RadarEventKind.Tap or RadarEventKind.Hold)
            .Select(item => new Point
            {
                Id = $"event:{item.EventId}", Time = item.StartTimeSeconds, Weight = 1,
                OwnerGroup = headOwners.TryGetValue(item.EventId, out var owner) ? owner : null
            }).ToList();
        var touches = events.Where(item => item.Kind is RadarEventKind.Touch or RadarEventKind.TouchHold).ToArray();
        var touchIndex = 0;
        output.AddRange(Workload.SimultaneousTouchPoints(touches, TouchWeight).Select(item => new Point
        {
            Id = $"touch:{touchIndex++}", Time = item.TimeSeconds, Weight = item.Weight
        }));
        foreach (var group in groups)
        {
            var index = 0;
            foreach (var launch in group.Events.Select(item => item.StartTimeSeconds).Distinct().OrderBy(value => value))
                output.Add(new Point
                {
                    Id = $"slide:{group.Key}:{index++}", Time = launch, Weight = 1, OwnerGroup = group.Key
                });
        }
        return output.OrderBy(item => item.Time).ThenBy(item => item.Id, StringComparer.Ordinal).ToList();
    }

    private static List<List<Point>> Assign(IReadOnlyList<Onset> onsets, IReadOnlyList<Point> points)
    {
        var memberKeys = onsets.SelectMany(item => item.Groups).Select(item => item.Key).ToHashSet();
        var assigned = Enumerable.Range(0, onsets.Count).Select(_ => new List<Point>()).ToList();
        foreach (var point in points)
        {
            if (point.OwnerGroup is int owner && memberKeys.Contains(owner)) continue;
            var candidates = new List<(double Distance, int Index)>();
            for (var index = 0; index < onsets.Count; index++)
            {
                var pending = onsets[index].Groups.Where(group =>
                    point.Time >= group.DeclarationTime - Tolerance &&
                    point.Time <= group.LaunchTime + Tolerance).ToArray();
                if (pending.Length > 0)
                    candidates.Add((pending.Min(group => Math.Max(0, group.LaunchTime - point.Time)), index));
            }
            if (candidates.Count > 0)
            {
                var selected = candidates.OrderBy(item => item.Distance).ThenBy(item => item.Index).First();
                assigned[selected.Index].Add(point);
            }
        }
        return assigned;
    }

    private static double OnsetLoad(Onset onset, IReadOnlyList<Point> points)
    {
        var launchTimes = onset.Groups.Select(item => item.LaunchTime).ToArray();
        var launchPoints = points.Where(point => launchTimes.Any(time => SameTime(point.Time, time))).ToArray();
        var launchIds = launchPoints.Select(item => item.Id).ToHashSet();
        var internalPoints = points.Where(item => !launchIds.Contains(item.Id)).ToArray();
        var internalLoad = 0.0;
        var previousCount = 0;
        foreach (var batch in Batches(internalPoints))
        {
            var nextCount = previousCount + batch.Count;
            var marginal = (InternalTotal(nextCount) - InternalTotal(previousCount)) / batch.Count;
            internalLoad += marginal * batch.Sum(item => item.Weight);
            previousCount = nextCount;
        }
        return internalLoad + launchPoints.Sum(item => item.Weight);
    }

    private static IReadOnlyList<IReadOnlyList<Point>> Batches(IReadOnlyList<Point> points)
    {
        var output = new List<IReadOnlyList<Point>>();
        foreach (var point in points)
        {
            if (output.Count == 0 || !SameTime(output[^1][0].Time, point.Time))
                output.Add(new List<Point>());
            ((List<Point>)output[^1]).Add(point);
        }
        return output;
    }

    private static double InternalTotal(int count)
    {
        var logFactorialBase2 = 0.0;
        for (var value = 2; value <= count; value++) logFactorialBase2 += Math.Log(value, 2);
        return count + Alpha * logFactorialBase2;
    }

    private static double EffectiveLength(int count)
    {
        if (count <= EffectiveLengthBase) return count;
        var scaled = 1 + (double)(count - EffectiveLengthBase) / EffectiveLengthBase;
        return EffectiveLengthBase * (1 + Math.Log(scaled, EffectiveLengthBase));
    }

    private static bool SameTime(double left, double right) => Math.Abs(left - right) <= Tolerance;
}
