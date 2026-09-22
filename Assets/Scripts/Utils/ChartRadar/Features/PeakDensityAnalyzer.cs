using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace MajdataPlay.Utils.ChartRadar;

internal sealed class PeakDensityAnalyzer : IRadarFeatureAnalyzer
{
    private const double StepSeconds = 0.5;
    private const double LeftWeight = 0.2;
    private const double CenterWeight = 0.6;
    private const double RightWeight = 0.2;
    private const double TouchGroupWeight = 0.5;
    private const int PeakCount = 3;
    private static readonly double[] PeakWeights = { 0.5, 0.3, 0.2 };
    private const double NeighborhoodSeconds = 3 * Workload.WindowSeconds;

    private sealed class ButtonOnset
    {
        internal double TimeSeconds { get; set; }
        internal BeatPosition Beat { get; set; }
        internal int Position { get; set; }
        internal double Weight { get; set; }
    }

    public RadarFeatureResult Analyze(AnalysisContext context)
    {
        context.ThrowIfCancellationRequested();
        if (context.DurationSeconds <= 0) return RadarFeatureResult.Failure("Chart duration must be positive.");
        return RadarFeatureResult.Success(PeakDensity(Points(context.Events), context.DurationSeconds));
    }

    private static IReadOnlyList<Workload.Point> Points(IReadOnlyList<RadarEvent> events)
    {
        var buttons = new List<ButtonOnset>();
        var slides = new List<RadarEvent>();
        var touches = new List<RadarEvent>();
        foreach (var item in events)
        {
            if (item.Kind is RadarEventKind.Tap or RadarEventKind.Hold)
            {
                var weight = item.Kind == RadarEventKind.Hold &&
                             Workload.BeatDuration(item) > Workload.EighthBeat ? 2 : 1;
                buttons.Add(new ButtonOnset
                {
                    TimeSeconds = item.StartTimeSeconds,
                    Beat = item.StartBeat,
                    Position = int.Parse(item.Position!),
                    Weight = weight
                });
            }
            else if (item.Kind == RadarEventKind.Slide) slides.Add(item);
            else if (item.Kind is RadarEventKind.Touch or RadarEventKind.TouchHold) touches.Add(item);
        }
        var output = DecaySweepButtons(buttons).ToList();
        output.AddRange(Workload.SlidePoints(slides, null));
        output.AddRange(Workload.TouchPoints(touches, TouchGroupWeight));
        return output;
    }

    private static IReadOnlyList<Workload.Point> DecaySweepButtons(IReadOnlyList<ButtonOnset> buttons)
    {
        var byBeat = buttons.GroupBy(item => item.Beat).OrderBy(group => group.Key);
        var output = new List<Workload.Point>();
        var runLength = 0;
        ButtonOnset? previous = null;
        BeatPosition? interval = null;
        int? direction = null;
        foreach (var group in byBeat)
        {
            var simultaneous = group.ToArray();
            if (simultaneous.Length != 1)
            {
                output.AddRange(simultaneous.Select(item => new Workload.Point(item.TimeSeconds, item.Weight)));
                runLength = 0; previous = null; interval = null; direction = null;
                continue;
            }
            var current = simultaneous[0];
            var currentDirection = previous is null ? null : Direction(previous.Position, current.Position);
            BeatPosition? currentInterval = previous is null ? null : current.Beat - previous.Beat;
            var continues = previous is not null && currentDirection is not null &&
                currentInterval > BeatPosition.Zero &&
                (runLength == 1 || currentDirection == direction && currentInterval == interval);
            if (continues)
            {
                runLength++;
                if (runLength == 2) { direction = currentDirection; interval = currentInterval; }
            }
            else if (previous is not null && currentDirection is not null && currentInterval > BeatPosition.Zero)
            {
                runLength = 2; direction = currentDirection; interval = currentInterval;
            }
            else
            {
                runLength = 1; direction = null; interval = null;
            }
            var divisor = Math.Log(Math.Max(2, runLength - 2), 2);
            output.Add(new Workload.Point(current.TimeSeconds, current.Weight / divisor));
            previous = current;
        }
        return output;
    }

    private static int? Direction(int left, int right)
    {
        if (right == left % 8 + 1) return 1;
        if (right == (left - 2 + 8) % 8 + 1) return -1;
        return null;
    }

    private static double PeakDensity(IReadOnlyList<Workload.Point> source, double duration)
    {
        if (source.Count == 0) return 0;
        var points = source.OrderBy(item => item.TimeSeconds).ThenBy(item => item.Weight).ToArray();
        var times = points.Select(item => item.TimeSeconds).ToArray();
        var prefix = new double[points.Length + 1];
        for (var index = 0; index < points.Length; index++) prefix[index + 1] = prefix[index] + points[index].Weight;
        double Density(double start)
        {
            var begin = LowerBound(times, start - 1e-9);
            var end = LowerBound(times, start + Workload.WindowSeconds - 1e-9);
            return (prefix[end] - prefix[begin]) / Workload.WindowSeconds;
        }
        var ratio = duration / StepSeconds;
        var nearest = Math.Round(ratio);
        if (Math.Abs(ratio - nearest) <= 1e-12) ratio = nearest;
        var candidates = Enumerable.Range(0, (int)Math.Floor(ratio) + 1)
            .Select(index =>
            {
                var start = index * StepSeconds;
                var score = LeftWeight * Density(start - Workload.WindowSeconds) +
                            CenterWeight * Density(start) +
                            RightWeight * Density(start + Workload.WindowSeconds);
                return (Score: score, Start: start);
            })
            .OrderByDescending(item => item.Score).ThenBy(item => item.Start);
        var selected = new List<(double Score, double Start)>();
        foreach (var candidate in candidates)
        {
            if (selected.All(item => Math.Abs(candidate.Start - item.Start) >= NeighborhoodSeconds))
                selected.Add(candidate);
            if (selected.Count == PeakCount) break;
        }
        return selected.Select((item, index) => item.Score * PeakWeights[index]).Sum();
    }

    private static int LowerBound(double[] values, double target)
    {
        var left = 0; var right = values.Length;
        while (left < right)
        {
            var middle = left + (right - left) / 2;
            if (values[middle] < target) left = middle + 1;
            else right = middle;
        }
        return left;
    }
}
