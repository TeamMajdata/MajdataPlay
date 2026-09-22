using System;
using System.Collections.Generic;
using System.Linq;
using SimaiRadar.Core;

#nullable enable

namespace SimaiRadar.Analysis.Features;

internal sealed class SlideSequenceAnalyzer : IRadarFeatureAnalyzer
{
    private const double CadenceReferenceSeconds = 0.5;
    private const double SimultaneousSeconds = 1.0 / 60;
    private static readonly BeatPosition MinimumInterval = new(1, 2);
    private const int LengthBase = 4;
    private const int LengthKnee = 16;
    private const double LengthKneeFactor = 3;
    private const int TopCount = 5;
    private const double ConcurrencyWeight = 0.5;
    private const double Tolerance = 1e-9;
    private static readonly double[] TopWeights = Enumerable.Range(1, TopCount)
        .Select(rank => 1 / Math.Log(rank + 1, 2)).ToArray();

    private sealed class Group
    {
        internal BeatPosition DeclarationBeat { get; set; }
        internal double DeclarationTime { get; set; }
        internal int PathCount { get; set; }
        internal int Key { get; set; }
    }

    private sealed class Cluster
    {
        internal BeatPosition DeclarationBeat { get; set; }
        internal double DeclarationTime { get; set; }
        internal IReadOnlyList<Group> Groups { get; set; } = Array.Empty<Group>();
    }

    private sealed class PathState
    {
        internal IReadOnlyList<int> Indexes { get; set; } = Array.Empty<int>();
        internal int DirectLinks { get; set; }
        internal double CadenceSum { get; set; }
        internal double ConcurrencySum { get; set; }
    }

    public RadarFeatureResult Analyze(AnalysisContext context)
    {
        context.ThrowIfCancellationRequested();
        if (context.DurationSeconds <= 0) return RadarFeatureResult.Failure("Chart duration must be positive.");
        var groups = Groups(context.Events);
        if (groups.Count == 0) return RadarFeatureResult.Success(0);
        var intensities = Sections(Clusters(groups)).Select(SectionIntensity)
            .Where(value => value > 0).OrderByDescending(value => value).Take(TopCount).ToArray();
        var weighted = intensities.Select((value, index) => value * TopWeights[index]).Sum();
        return RadarFeatureResult.Success(weighted / TopWeights.Sum());
    }

    private static List<Group> Groups(IReadOnlyList<RadarEvent> events)
    {
        var output = new List<Group>();
        foreach (var grouping in events.Where(item =>
                         item.Kind == RadarEventKind.Slide && item.SlideGroupId is not null)
                     .GroupBy(item => item.SlideGroupId!.Value))
        {
            var valid = grouping.Where(item => item.EndTimeSeconds > item.StartTimeSeconds).ToArray();
            if (valid.Length == 0) continue;
            var times = valid.Select(item => item.SlideDeclareTimeSeconds).Distinct().ToArray();
            var beats = valid.Select(item => item.SlideDeclareBeat).Distinct().ToArray();
            if (times.Length != 1 || times[0] is null || beats.Length != 1 || beats[0] is null)
                throw new InvalidOperationException("Slide group declaration metadata is inconsistent.");
            output.Add(new Group
            {
                Key = grouping.Key,
                DeclarationTime = times[0]!.Value,
                DeclarationBeat = beats[0]!.Value,
                PathCount = valid.Length
            });
        }
        return output.OrderBy(item => item.DeclarationBeat)
            .ThenBy(item => item.DeclarationTime).ThenBy(item => item.Key).ToList();
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

    private static IReadOnlyList<IReadOnlyList<Cluster>> Sections(IReadOnlyList<Cluster> clusters)
    {
        var output = new List<IReadOnlyList<Cluster>>();
        var current = new List<Cluster> { clusters[0] };
        foreach (var cluster in clusters.Skip(1))
        {
            var delta = (cluster.DeclarationBeat - current[^1].DeclarationBeat).ToDouble();
            if (delta > Tolerance && delta <= 1 + Tolerance) current.Add(cluster);
            else { output.Add(current); current = new List<Cluster> { cluster }; }
        }
        output.Add(current);
        return output;
    }

    private static double SectionIntensity(IReadOnlyList<Cluster> section)
    {
        var selected = SequenceOnsets(section);
        var cadence = selected.Count == 1 ? 1 : selected.Zip(selected.Skip(1), (left, right) =>
        {
            var interval = right.DeclarationTime - left.DeclarationTime;
            if (interval <= 0) throw new InvalidOperationException("Distinct Slide onsets must advance in time.");
            return CadenceReferenceSeconds / interval;
        }).Average();
        var concurrency = selected.Average(Concurrency);
        var continuous = selected.Count >= 2 ? cadence * LengthFactor(selected.Count) : 0;
        return continuous + ConcurrencyWeight * concurrency;
    }

    private static IReadOnlyList<Cluster> SequenceOnsets(IReadOnlyList<Cluster> section)
    {
        var bestAt = new List<Dictionary<bool, PathState>>();
        for (var index = 0; index < section.Count; index++)
        {
            var current = new Dictionary<bool, PathState>
            {
                [false] = new PathState
                {
                    Indexes = new[] { index },
                    ConcurrencySum = Concurrency(section[index])
                }
            };
            for (var previous = index - 1; previous >= 0; previous--)
            {
                var gap = section[index].DeclarationBeat - section[previous].DeclarationBeat;
                if (gap > new BeatPosition(1)) break;
                if (gap < MinimumInterval) continue;
                var elapsed = section[index].DeclarationTime - section[previous].DeclarationTime;
                if (elapsed <= 0) throw new InvalidOperationException("Distinct Slide onsets must advance in time.");
                foreach (var pair in bestAt[previous])
                {
                    var direct = previous == index - 1;
                    var candidate = new PathState
                    {
                        Indexes = pair.Value.Indexes.Concat(new[] { index }).ToArray(),
                        DirectLinks = pair.Value.DirectLinks + (direct ? 1 : 0),
                        CadenceSum = pair.Value.CadenceSum + CadenceReferenceSeconds / elapsed,
                        ConcurrencySum = pair.Value.ConcurrencySum + Concurrency(section[index])
                    };
                    var key = pair.Key || direct;
                    if (!current.TryGetValue(key, out var incumbent) || Compare(candidate, incumbent) > 0)
                        current[key] = candidate;
                }
            }
            bestAt.Add(current);
        }
        var eligible = bestAt.Where(item => item.ContainsKey(true)).Select(item => item[true]).ToArray();
        IReadOnlyList<int> indexes;
        if (eligible.Length > 0)
            indexes = eligible.Aggregate((best, item) => Compare(item, best) > 0 ? item : best).Indexes;
        else
            indexes = new[] { Enumerable.Range(0, section.Count)
                .OrderByDescending(index => Concurrency(section[index])).ThenBy(index => index).First() };
        return indexes.Select(index => section[index]).ToArray();
    }

    private static int Compare(PathState left, PathState right)
    {
        var comparisons = new[]
        {
            left.Indexes.Count.CompareTo(right.Indexes.Count),
            left.DirectLinks.CompareTo(right.DirectLinks),
            left.CadenceSum.CompareTo(right.CadenceSum),
            left.ConcurrencySum.CompareTo(right.ConcurrencySum)
        };
        foreach (var comparison in comparisons) if (comparison != 0) return comparison;
        for (var index = 0; index < Math.Min(left.Indexes.Count, right.Indexes.Count); index++)
        {
            var comparison = right.Indexes[index].CompareTo(left.Indexes[index]);
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    private static double Concurrency(Cluster cluster) =>
        Math.Max(0, cluster.Groups.Sum(group => Math.Sqrt(group.PathCount)) - 1);

    private static double LengthFactor(int count)
    {
        if (count <= LengthBase) return 1;
        if (count <= LengthKnee)
            return 1 + (LengthKneeFactor - 1) * (double)(count - LengthBase) / (LengthKnee - LengthBase);
        return LengthKneeFactor * Math.Sqrt((double)count / LengthKnee);
    }
}
