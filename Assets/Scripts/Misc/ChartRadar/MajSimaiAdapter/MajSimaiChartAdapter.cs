using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MajSimai;
using SimaiRadar.Core;

#nullable enable

namespace SimaiRadar.MajSimaiAdapter;

public sealed class AdaptationResult
{
    public RadarChartInput? Chart { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
    public bool IsSuccess => Chart is not null && Errors.Count == 0;
}

public sealed class MajSimaiChartAdapter
{
    private const double TimeTolerance = 1e-8;
    private const int MaximumBeatDenominator = 4096;
    private const double BeatSnapTolerance = 1e-7;
    private readonly SlidePathResolver _slidePaths = new();

    public async Task<AdaptationResult> ParseAndAdaptAsync(string inote)
    {
        try
        {
            var chart = await SimaiParser.ParseChartAsync(inote).ConfigureAwait(false);
            return Adapt(chart);
        }
        catch (Exception exception)
        {
            return Failure($"MajSimai parse failed: {exception.Message}");
        }
    }

    public AdaptationResult Adapt(SimaiChart chart)
    {
        try
        {
            var commaTimings = chart.CommaTimings.ToArray();
            if (commaTimings.Length == 0)
                return Failure("MajSimai returned no comma timing points.");

            var timeline = BuildTimeline(commaTimings);
            var pending = new List<PendingEvent>();
            AddTimingEvents(timeline, pending);

            var declarationOrder = pending.Count;
            var slideGroupId = 0;
            foreach (var timing in chart.NoteTimings)
            {
                RejectAmbiguousNoHeadGrouping(timing);
                var declarationBeat = timeline.BeatAt(timing.Timing);
                var declarationTime = timeline.TimeAt(declarationBeat);
                PendingEvent? currentHead = null;
                int? currentSlideGroupId = null;
                int? currentSlideStartPosition = null;
                foreach (var note in timing.Notes)
                {
                    declarationOrder++;
                    if (note.Type == SimaiNoteType.Slide)
                    {
                        if (!note.IsSlideNoHead)
                        {
                            currentHead = CreateHead(
                                note, timing, declarationTime, declarationBeat, declarationOrder);
                            pending.Add(currentHead);
                            currentSlideGroupId = ++slideGroupId;
                            currentSlideStartPosition = note.StartPosition;
                        }
                        else if (currentSlideGroupId is null ||
                                 currentSlideStartPosition != note.StartPosition)
                        {
                            currentHead = null;
                            currentSlideGroupId = ++slideGroupId;
                            currentSlideStartPosition = note.StartPosition;
                        }

                        pending.Add(CreateSlide(
                            note, timing, declarationTime, declarationBeat, declarationOrder,
                            currentHead?.TemporaryId, currentSlideGroupId.Value, timeline));
                        continue;
                    }

                    currentHead = null;
                    currentSlideGroupId = null;
                    currentSlideStartPosition = null;
                    pending.Add(CreateOrdinary(
                        note, timing, declarationTime, declarationBeat, declarationOrder, timeline));
                }
            }

            var ordered = pending
                .OrderBy(item => item.Event.StartTimeSeconds)
                .ThenBy(item => item.SourcePosition)
                .ThenBy(item => item.Event.DeclarationOrder)
                .ToArray();
            var idMap = new Dictionary<int, int>();
            for (var index = 0; index < ordered.Length; index++)
                idMap[ordered[index].TemporaryId] = index + 1;

            var events = new List<RadarEvent>(ordered.Length);
            for (var index = 0; index < ordered.Length; index++)
            {
                var source = ordered[index].Event;
                events.Add(new RadarEvent
                {
                    EventId = index + 1,
                    Kind = source.Kind,
                    IsSlideHead = source.IsSlideHead,
                    SlideDeclareTimeSeconds = source.SlideDeclareTimeSeconds,
                    SlideDeclareBeat = source.SlideDeclareBeat,
                    StartTimeSeconds = source.StartTimeSeconds,
                    EndTimeSeconds = source.EndTimeSeconds,
                    StartBeat = source.StartBeat,
                    EndBeat = source.EndBeat,
                    Bpm = source.Bpm,
                    Position = source.Position,
                    HeadEventId = ordered[index].HeadTemporaryId is int head ? idMap[head] : null,
                    SlideGroupId = source.SlideGroupId,
                    SlidePath = source.SlidePath,
                    IsBreak = source.IsBreak,
                    IsEx = source.IsEx,
                    IsMine = source.IsMine,
                    Flags = source.Flags,
                    RawToken = source.RawToken,
                    DeclarationOrder = source.DeclarationOrder
                });
            }

            var objects = events.Where(item => item.Kind != RadarEventKind.Timing).ToArray();
            return new AdaptationResult
            {
                Chart = new RadarChartInput
                {
                    Events = events,
                    ChartEndTimeSeconds = timeline.Points[^1].CanonicalTime,
                    LastEventEndTimeSeconds = objects.Length == 0
                        ? null
                        : objects.Max(item => item.EndTimeSeconds)
                }
            };
        }
        catch (Exception exception)
        {
            return Failure($"MajSimai output could not be adapted safely: {exception.Message}");
        }
    }

    private PendingEvent CreateSlide(
        SimaiNote note,
        SimaiTimingPoint timing,
        double declarationTime,
        BeatPosition declarationBeat,
        int order,
        int? headTemporaryId,
        int slideGroupId,
        Timeline timeline)
    {
        var startBeat = timeline.BeatAt(note.SlideStartTime);
        var endBeat = timeline.BeatAt(note.SlideStartTime + note.SlideTime);
        var startTime = timeline.TimeAt(startBeat);
        var endTime = timeline.TimeAt(endBeat);
        return NewPending(new RadarEvent
        {
            EventId = 0,
            Kind = RadarEventKind.Slide,
            IsSlideHead = false,
            SlideDeclareTimeSeconds = declarationTime,
            SlideDeclareBeat = declarationBeat,
            StartTimeSeconds = startTime,
            EndTimeSeconds = endTime,
            StartBeat = startBeat,
            EndBeat = endBeat,
            Position = note.StartPosition.ToString(),
            SlideGroupId = slideGroupId,
            SlidePath = _slidePaths.Resolve(note.RawContent, startTime, endTime),
            IsBreak = note.IsSlideBreak,
            IsEx = false,
            IsMine = note.IsMineSlide,
            Flags = Flags(note),
            RawToken = note.RawContent,
            DeclarationOrder = order
        }, timing.RawTextPosition, headTemporaryId);
    }

    private static PendingEvent CreateHead(
        SimaiNote note,
        SimaiTimingPoint timing,
        double declarationTime,
        BeatPosition declarationBeat,
        int order) =>
        NewPending(new RadarEvent
        {
            EventId = 0,
            Kind = RadarEventKind.Tap,
            IsSlideHead = true,
            StartTimeSeconds = declarationTime,
            EndTimeSeconds = declarationTime,
            StartBeat = declarationBeat,
            EndBeat = declarationBeat,
            Position = note.StartPosition.ToString(),
            IsBreak = note.IsBreak,
            IsEx = note.IsEx,
            IsMine = note.IsMine,
            Flags = Flags(note),
            RawToken = note.RawContent,
            DeclarationOrder = order
        }, timing.RawTextPosition);

    private static PendingEvent CreateOrdinary(
        SimaiNote note,
        SimaiTimingPoint timing,
        double declarationTime,
        BeatPosition declarationBeat,
        int order,
        Timeline timeline)
    {
        var kind = note.Type switch
        {
            SimaiNoteType.Tap => RadarEventKind.Tap,
            SimaiNoteType.Hold => RadarEventKind.Hold,
            SimaiNoteType.Touch => RadarEventKind.Touch,
            SimaiNoteType.TouchHold => RadarEventKind.TouchHold,
            _ => throw new MajSimaiAdaptationException($"Unsupported MajSimai note type: {note.Type}")
        };
        var endBeat = timeline.BeatAt(timing.Timing + note.HoldTime);
        var endTime = timeline.TimeAt(endBeat);
        return NewPending(new RadarEvent
        {
            EventId = 0,
            Kind = kind,
            IsSlideHead = false,
            StartTimeSeconds = declarationTime,
            EndTimeSeconds = endTime,
            StartBeat = declarationBeat,
            EndBeat = endBeat,
            Position = kind is RadarEventKind.Touch or RadarEventKind.TouchHold
                ? TouchPosition(note)
                : note.StartPosition.ToString(),
            IsBreak = note.IsBreak,
            IsEx = note.IsEx,
            IsMine = note.IsMine,
            Flags = Flags(note),
            RawToken = note.RawContent,
            DeclarationOrder = order
        }, timing.RawTextPosition);
    }

    private static string TouchPosition(SimaiNote note) =>
        note.TouchArea == 'C' ? "C" : $"{note.TouchArea}{note.StartPosition}";

    private static void RejectAmbiguousNoHeadGrouping(SimaiTimingPoint timing)
    {
        if (timing.RawContent.IndexOf('?') < 0 && timing.RawContent.IndexOf('!') < 0) return;

        var slides = timing.Notes.Where(note => note.Type == SimaiNoteType.Slide).ToArray();
        var explicitStarts = new HashSet<int>(
            slides.Where(note => !note.IsSlideNoHead).Select(note => note.StartPosition));
        if (slides.Any(note => note.IsSlideNoHead && explicitStarts.Contains(note.StartPosition)))
        {
            throw new MajSimaiAdaptationException(
                "MajSimai output cannot distinguish a same-head branch from an independent " +
                "no-head Slide at the same timing and position. The chart was not analyzed.");
        }
    }

    private static IReadOnlyDictionary<string, bool> Flags(SimaiNote note) =>
        new Dictionary<string, bool>
        {
            ["using_sv"] = note.UsingSV,
            ["force_star"] = note.IsForceStar,
            ["fake_rotate"] = note.IsFakeRotate,
            ["hanabi"] = note.IsHanabi,
            ["tap_head"] = note.IsTapHeadSlide
        };

    private static void AddTimingEvents(Timeline timeline, List<PendingEvent> output)
    {
        float? previous = null;
        var order = 0;
        foreach (var point in timeline.Points)
        {
            if (previous is not null && Math.Abs(previous.Value - point.Bpm) < 1e-6) continue;
            previous = point.Bpm;
            output.Add(NewPending(new RadarEvent
            {
                EventId = 0,
                Kind = RadarEventKind.Timing,
                StartTimeSeconds = point.CanonicalTime,
                EndTimeSeconds = point.CanonicalTime,
                StartBeat = point.Beat,
                EndBeat = point.Beat,
                Bpm = point.Bpm,
                RawToken = string.Empty,
                DeclarationOrder = order++
            }, point.SourcePosition));
        }
    }

    private Timeline BuildTimeline(SimaiTimingPoint[] commaTimings)
    {
        var points = new List<TimelinePoint>(commaTimings.Length);
        var beatValue = 0.0;
        var canonicalTime = 0.0;
        for (var index = 0; index < commaTimings.Length; index++)
        {
            var point = commaTimings[index];
            if (double.IsNaN(point.Timing) || double.IsInfinity(point.Timing) ||
                float.IsNaN(point.Bpm) || float.IsInfinity(point.Bpm) || point.Bpm <= 0)
                throw new MajSimaiAdaptationException("MajSimai returned a non-finite or non-positive timing value.");
            if (index > 0)
            {
                var previous = commaTimings[index - 1];
                var elapsed = point.Timing - previous.Timing;
                if (elapsed < -TimeTolerance)
                    throw new MajSimaiAdaptationException("MajSimai comma timings are not ordered.");
                beatValue += elapsed * previous.Bpm / 60.0;
            }
            var beat = SnapBeatValue(beatValue);
            if (index > 0)
            {
                var previous = points[index - 1];
                canonicalTime = previous.CanonicalTime +
                    (beat - previous.Beat).ToDouble() * 60.0 / previous.Bpm;
            }
            points.Add(new TimelinePoint(
                point.Timing, canonicalTime, beat, point.Bpm, point.RawTextPosition));
        }
        return new Timeline(points);
    }

    private BeatPosition SnapBeatValue(double beatValue) =>
        BeatPosition.SnapFromDouble(
            beatValue,
            MaximumBeatDenominator,
            BeatSnapTolerance);

    private static PendingEvent NewPending(RadarEvent item, int sourcePosition, int? head = null) =>
        new(item, sourcePosition, head, PendingEvent.NextId());

    private static AdaptationResult Failure(string message) =>
        new() { Errors = new[] { message } };

    private sealed class Timeline
    {
        public IReadOnlyList<TimelinePoint> Points { get; }
        public Timeline(IReadOnlyList<TimelinePoint> points) => Points = points;

        public BeatPosition BeatAt(double time)
        {
            for (var index = Points.Count - 1; index >= 0; index--)
            {
                var point = Points[index];
                if (Math.Abs(time - point.RawTime) <= TimeTolerance) return point.Beat;
                if (time > point.RawTime)
                    return BeatPosition.SnapFromDouble(
                        point.Beat.ToDouble() + (time - point.RawTime) * point.Bpm / 60.0,
                        MaximumBeatDenominator,
                        BeatSnapTolerance);
            }
            throw new MajSimaiAdaptationException($"Time {time} precedes the MajSimai timeline.");
        }

        public double TimeAt(BeatPosition beat)
        {
            for (var index = Points.Count - 1; index >= 0; index--)
            {
                var point = Points[index];
                if (point.Beat <= beat)
                    return point.CanonicalTime + (beat - point.Beat).ToDouble() * 60.0 / point.Bpm;
            }
            throw new MajSimaiAdaptationException($"Beat {beat} precedes the MajSimai timeline.");
        }
    }

    private sealed class TimelinePoint
    {
        public TimelinePoint(
            double rawTime,
            double canonicalTime,
            BeatPosition beat,
            float bpm,
            int sourcePosition)
        {
            RawTime = rawTime;
            CanonicalTime = canonicalTime;
            Beat = beat;
            Bpm = bpm;
            SourcePosition = sourcePosition;
        }

        public double RawTime { get; }
        public double CanonicalTime { get; }
        public BeatPosition Beat { get; }
        public float Bpm { get; }
        public int SourcePosition { get; }
    }

    private sealed class PendingEvent
    {
        private static int _nextId;

        public PendingEvent(RadarEvent item, int sourcePosition, int? headTemporaryId, int temporaryId)
        {
            Event = item;
            SourcePosition = sourcePosition;
            HeadTemporaryId = headTemporaryId;
            TemporaryId = temporaryId;
        }

        public RadarEvent Event { get; }
        public int SourcePosition { get; }
        public int? HeadTemporaryId { get; }
        public int TemporaryId { get; }
        public static int NextId() => Interlocked.Increment(ref _nextId);
    }
}
