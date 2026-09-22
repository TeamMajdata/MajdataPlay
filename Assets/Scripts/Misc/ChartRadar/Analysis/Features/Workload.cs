using System;
using System.Collections.Generic;
using System.Linq;
using SimaiRadar.Core;

#nullable enable

namespace SimaiRadar.Analysis.Features;

internal static class Workload
{
    internal const double WindowSeconds = 1.5;
    internal const int SlideLengthUnit = 64;
    internal static readonly BeatPosition EighthBeat = new(1, 2);
    internal static readonly BeatPosition SixteenthBeat = new(1, 4);

    private static readonly IReadOnlyDictionary<string, HashSet<string>> TouchAdjacency =
        BuildTouchAdjacency();

    internal readonly struct Point
    {
        internal Point(double timeSeconds, double weight)
        {
            TimeSeconds = timeSeconds;
            Weight = weight;
        }
        internal double TimeSeconds { get; }
        internal double Weight { get; }
    }

    private sealed class TouchComponent
    {
        internal int Id { get; set; }
        internal BeatPosition Beat { get; set; }
        internal double TimeSeconds { get; set; }
        internal IReadOnlyList<string> Positions { get; set; } = Array.Empty<string>();
    }

    internal static BeatPosition BeatDuration(RadarEvent item)
    {
        var duration = item.EndBeat - item.StartBeat;
        if (duration < BeatPosition.Zero) throw new InvalidOperationException("Hold has a negative beat interval.");
        return duration;
    }

    internal static IReadOnlyList<Point> CorrectedPoints(IReadOnlyList<RadarEvent> events)
    {
        var output = new List<Point>();
        var slides = new List<RadarEvent>();
        var touches = new List<RadarEvent>();
        foreach (var item in events)
        {
            switch (item.Kind)
            {
                case RadarEventKind.Tap:
                    output.Add(new Point(item.StartTimeSeconds, 1));
                    break;
                case RadarEventKind.Hold:
                    output.Add(new Point(item.StartTimeSeconds, BeatDuration(item) <= EighthBeat ? 1 : 2));
                    break;
                case RadarEventKind.Slide:
                    slides.Add(item);
                    break;
                case RadarEventKind.Touch:
                case RadarEventKind.TouchHold:
                    touches.Add(item);
                    break;
            }
        }
        output.AddRange(SlidePoints(slides, SlideLengthUnit));
        output.AddRange(TouchPoints(touches, 1));
        return output;
    }

    internal static IReadOnlyList<Point> SlidePoints(
        IReadOnlyList<RadarEvent> slides,
        int? lengthUnit)
    {
        var groups = slides.GroupBy(item => item.SlideGroupId ??
            throw new InvalidOperationException("Slide is missing its local group id."));
        var output = new List<Point>();
        foreach (var group in groups)
        {
            var totalLength = group.Sum(item => item.SlidePath?.Sum(segment => segment.BarCount) ?? 0);
            if (totalLength <= 0) throw new InvalidOperationException("Slide group has no positive length.");
            var weight = lengthUnit is null
                ? 1.0
                : (double)((totalLength + lengthUnit.Value - 1) / lengthUnit.Value);
            output.Add(new Point(group.Min(item => item.StartTimeSeconds), weight));
        }
        return output;
    }

    internal static IReadOnlyList<Point> TouchPoints(
        IReadOnlyList<RadarEvent> touches,
        double groupWeight)
    {
        if (touches.Count == 0) return Array.Empty<Point>();
        var components = BuildTouchComponents(touches);
        var beats = components.Keys.ToArray();
        var used = new HashSet<int>();
        var output = new List<Point>();
        for (var leftIndex = 0; leftIndex < beats.Length; leftIndex++)
        {
            var leftBeat = beats[leftIndex];
            for (var rightIndex = leftIndex + 1; rightIndex < beats.Length; rightIndex++)
            {
                var rightBeat = beats[rightIndex];
                if (rightBeat - leftBeat > SixteenthBeat) break;
                var left = components[leftBeat].Where(item => !used.Contains(item.Id)).ToArray();
                var right = components[rightBeat].Where(item => !used.Contains(item.Id)).ToArray();
                if (left.Length == 0 || right.Length == 0) continue;
                foreach (var group in CrossTimeComponents(left, right))
                {
                    foreach (var item in group) used.Add(item.Id);
                    output.Add(new Point(group.Min(item => item.TimeSeconds), groupWeight));
                }
            }
        }
        foreach (var item in components.Values.SelectMany(items => items))
            if (!used.Contains(item.Id)) output.Add(new Point(item.TimeSeconds, groupWeight));
        return output.OrderBy(item => item.TimeSeconds).ToArray();
    }

    internal static IReadOnlyList<Point> SimultaneousTouchPoints(
        IReadOnlyList<RadarEvent> touches,
        double weight) => BuildTouchComponents(touches).Values
            .SelectMany(items => items)
            .Select(item => new Point(item.TimeSeconds, weight))
            .OrderBy(item => item.TimeSeconds)
            .ToArray();

    private static SortedDictionary<BeatPosition, List<TouchComponent>> BuildTouchComponents(
        IReadOnlyList<RadarEvent> touches)
    {
        var byBeat = touches.GroupBy(item => item.StartBeat).OrderBy(group => group.Key).ToArray();
        var components = new SortedDictionary<BeatPosition, List<TouchComponent>>();
        var nextId = 0;
        foreach (var beatGroup in byBeat)
        {
            var simultaneous = beatGroup.ToArray();
            var groups = GraphComponents(simultaneous, (left, right) => Adjacent(left.Position!, right.Position!));
            components[beatGroup.Key] = groups.Select(group => new TouchComponent
            {
                Id = ++nextId,
                Beat = beatGroup.Key,
                TimeSeconds = group.Min(item => item.StartTimeSeconds),
                Positions = group.Select(item => item.Position!).ToArray()
            }).ToList();
        }
        return components;
    }

    private static IReadOnlyList<IReadOnlyList<TouchComponent>> CrossTimeComponents(
        IReadOnlyList<TouchComponent> left,
        IReadOnlyList<TouchComponent> right)
    {
        var nodes = left.Concat(right).ToArray();
        var leftIds = left.Select(item => item.Id).ToHashSet();
        var rightIds = right.Select(item => item.Id).ToHashSet();
        return GraphComponents(nodes, (first, second) =>
        {
            var crosses = leftIds.Contains(first.Id) && rightIds.Contains(second.Id) ||
                          rightIds.Contains(first.Id) && leftIds.Contains(second.Id);
            return crosses && first.Positions.Any(a => second.Positions.Any(b => Adjacent(a, b)));
        }).Where(group => group.Count > 1).ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<T>> GraphComponents<T>(
        IReadOnlyList<T> nodes,
        Func<T, T, bool> connected)
    {
        var remaining = new SortedSet<int>(Enumerable.Range(0, nodes.Count));
        var output = new List<IReadOnlyList<T>>();
        while (remaining.Count > 0)
        {
            var first = remaining.Min;
            remaining.Remove(first);
            var stack = new Stack<int>();
            stack.Push(first);
            var indexes = new List<int>();
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                indexes.Add(current);
                var neighbors = remaining.Where(candidate => connected(nodes[current], nodes[candidate])).ToArray();
                foreach (var neighbor in neighbors)
                {
                    remaining.Remove(neighbor);
                    stack.Push(neighbor);
                }
            }
            indexes.Sort();
            output.Add(indexes.Select(index => nodes[index]).ToArray());
        }
        return output;
    }

    private static bool Adjacent(string left, string right) =>
        left != right && TouchAdjacency.TryGetValue(left, out var neighbors) && neighbors.Contains(right);

    private static IReadOnlyDictionary<string, HashSet<string>> BuildTouchAdjacency()
    {
        var graph = new Dictionary<string, HashSet<string>> { ["C"] = new HashSet<string>() };
        foreach (var family in "ABDE")
            for (var index = 1; index <= 8; index++) graph[$"{family}{index}"] = new HashSet<string>();

        void Connect(string left, string right)
        {
            graph[left].Add(right);
            graph[right].Add(left);
        }
        int Ring(int index) => (index - 1 + 8) % 8 + 1;
        for (var index = 1; index <= 8; index++)
        {
            var previous = Ring(index - 1);
            var following = Ring(index + 1);
            var a = $"A{index}";
            var b = $"B{index}";
            var d = $"D{index}";
            var e = $"E{index}";
            Connect(d, $"A{previous}"); Connect(d, a);
            Connect(e, $"A{previous}"); Connect(e, a);
            Connect(e, $"B{previous}"); Connect(e, b);
            Connect(a, b); Connect(b, $"B{previous}"); Connect(b, $"B{following}"); Connect(b, "C");
        }
        return graph;
    }
}
