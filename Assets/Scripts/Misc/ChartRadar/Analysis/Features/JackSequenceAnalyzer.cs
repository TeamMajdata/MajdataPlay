using System;
using System.Collections.Generic;
using System.Linq;
using SimaiRadar.Core;

#nullable enable

namespace SimaiRadar.Analysis.Features;

internal sealed class JackSequenceAnalyzer : IRadarFeatureAnalyzer
{
    private const int TopK = 5;
    private const int MaxInterruptingTaps = 4;
    private const double InterruptingTapWeight = 1.5;
    private const double ExNoteWeight = 0.3;
    private const double SpeedReferenceSixteenthBpm = 180;
    private const double SpeedExponent = 1.5;
    private const double TopOneMultiplier = 1.3;
    private const double TopRankDecay = 0.645;

    private sealed class Batch
    {
        internal BeatPosition Beat { get; set; }
        internal double TimeSeconds { get; set; }
        internal IReadOnlyList<RadarEvent> Events { get; set; } = Array.Empty<RadarEvent>();
    }

    private sealed class Sequence
    {
        internal string Position { get; set; } = string.Empty;
        internal BeatPosition StartBeat { get; set; }
        internal int AnchorCount { get; set; }
        internal int ExAnchorCount { get; set; }
        internal int Interruptions { get; set; }
        internal int ExInterruptions { get; set; }
        internal double SpeedFactor { get; set; }
        internal double Strength => AnchorCount - ExAnchorCount + ExNoteWeight * ExAnchorCount +
            InterruptingTapWeight * (Interruptions - ExInterruptions + ExNoteWeight * ExInterruptions);
        internal double WeightedStrength => Strength * SpeedFactor;
    }

    public RadarFeatureResult Analyze(AnalysisContext context)
    {
        context.ThrowIfCancellationRequested();
        if (context.DurationSeconds <= 0) return RadarFeatureResult.Failure("Chart duration must be positive.");
        var sequences = Sequences(context.Events);
        var score = sequences.Take(TopK).Select((item, index) =>
            item.WeightedStrength * TopOneMultiplier * Math.Pow(TopRankDecay, index)).Sum();
        return RadarFeatureResult.Success(score);
    }

    private static IReadOnlyList<Sequence> Sequences(IReadOnlyList<RadarEvent> events)
    {
        var batches = events
            .Where(item => item.Kind is RadarEventKind.Tap or RadarEventKind.Hold)
            .GroupBy(item => item.StartBeat)
            .OrderBy(group => group.Key)
            .Select(group => new Batch
            {
                Beat = group.Key,
                TimeSeconds = group.Min(item => item.StartTimeSeconds),
                Events = group.OrderBy(item => item.EventId).ToArray()
            }).ToArray();
        var output = new List<Sequence>();
        for (var position = 1; position <= 8; position++)
            output.AddRange(PositionSequences(batches, position.ToString()));
        return output.OrderByDescending(item => item.WeightedStrength)
            .ThenByDescending(item => item.Strength)
            .ThenByDescending(item => item.AnchorCount)
            .ThenBy(item => item.StartBeat)
            .ThenBy(item => int.Parse(item.Position)).ToArray();
    }

    private static IReadOnlyList<Sequence> PositionSequences(IReadOnlyList<Batch> batches, string position)
    {
        var output = new List<Sequence>();
        BeatPosition? startBeat = null, previousBeat = null, pendingStartBeat = null;
        double? startTime = null, endTime = null;
        var anchorCount = 0; var exAnchorCount = 0; var anchorTimeCount = 0;
        var anchorsSinceInterruption = 0; var pending = 0; var pendingEx = 0;
        var committed = 0; var committedEx = 0;

        void Finish()
        {
            if (anchorTimeCount >= 2)
            {
                var span = endTime!.Value - startTime!.Value;
                if (span <= 0) throw new InvalidOperationException("Jack sequence must advance in time.");
                var bpm = 15 * (anchorTimeCount - 1) / span;
                output.Add(new Sequence
                {
                    Position = position,
                    StartBeat = startBeat!.Value,
                    AnchorCount = anchorCount,
                    ExAnchorCount = exAnchorCount,
                    Interruptions = committed,
                    ExInterruptions = committedEx,
                    SpeedFactor = Math.Pow(bpm / SpeedReferenceSixteenthBpm, SpeedExponent)
                });
            }
            startBeat = previousBeat = pendingStartBeat = null;
            startTime = endTime = null;
            anchorCount = exAnchorCount = anchorTimeCount = anchorsSinceInterruption = 0;
            pending = pendingEx = committed = committedEx = 0;
        }

        foreach (var batch in batches)
        {
            var anchors = batch.Events.Where(item => item.Position == position).ToArray();
            if (previousBeat is not null && batch.Beat - previousBeat.Value > Workload.EighthBeat) Finish();
            if (anchors.Length > 0)
            {
                var returned = pendingStartBeat is not null;
                if (returned && batch.Beat - pendingStartBeat!.Value > Workload.EighthBeat)
                {
                    Finish(); returned = false;
                }
                else if (returned)
                {
                    committed += pending; committedEx += pendingEx;
                }
                if (startBeat is null) { startBeat = batch.Beat; startTime = batch.TimeSeconds; }
                endTime = batch.TimeSeconds;
                anchorCount += anchors.Length;
                exAnchorCount += anchors.Count(item => item.IsEx == true);
                anchorTimeCount++;
                anchorsSinceInterruption = returned ? 1 : anchorsSinceInterruption + 1;
                pending = pendingEx = 0; pendingStartBeat = null; previousBeat = batch.Beat;
                continue;
            }
            if (startBeat is null) continue;
            if (batch.Events.Any(item => item.Kind != RadarEventKind.Tap) ||
                pendingStartBeat is null && anchorsSinceInterruption < 2 ||
                pending + batch.Events.Count > MaxInterruptingTaps)
            {
                Finish(); continue;
            }
            pendingStartBeat ??= batch.Beat;
            pending += batch.Events.Count;
            pendingEx += batch.Events.Count(item => item.IsEx == true);
            previousBeat = batch.Beat;
        }
        Finish();
        return output;
    }
}
