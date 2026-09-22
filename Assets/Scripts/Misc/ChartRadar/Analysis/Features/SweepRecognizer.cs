using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SimaiRadar.Core;

#nullable enable

namespace SimaiRadar.Analysis.Features;

/// <summary>
/// Recognizes maximal one-spine, variable-width button sweeps. Scan states and
/// prefix candidates are persistent/lightweight; only selected sequences expand
/// their per-batch arrays.
/// </summary>
internal static class SweepRecognizer
{
    internal const int MaximumStates = 300_000;
    internal const int MaximumButtonAttacks = 30_000;
    internal const int MaximumLightweightCandidates = 50_000;
    internal const int MaximumSelectionStates = 100_000;
    internal const double SpeedRelativeTolerance = 0.005;
    private static readonly BeatPosition ShortHoldMaximum = new(1, 4);
    private static readonly BeatPosition BaseMaximumUnit = new(1, 3);
    private static readonly BeatPosition EighthBeat = new(1, 2);
    private const double TimeTolerance = 1e-9;

    private sealed class HoldOccupancy
    {
        internal int Lane { get; set; }
        internal BeatPosition Start { get; set; }
        internal BeatPosition End { get; set; }
    }

    private sealed class AttackBatch
    {
        internal BeatPosition Beat { get; set; }
        internal double Time { get; set; }
        internal IReadOnlyList<int> AttackIds { get; set; } = Array.Empty<int>();
    }

    private sealed class SpineState
    {
        internal SpineState? Previous { get; set; }
        internal int BatchIndex { get; set; }
        internal int StartBatchIndex { get; set; }
        internal int EntryAttackId { get; set; }
        internal int ExitAttackId { get; set; }
        internal int Length { get; set; }
        internal int? Direction { get; set; }
        internal int RunSteps { get; set; }
        internal int TurnCount { get; set; }
        internal BeatPosition? UnitBeatInterval { get; set; }
        internal double? UnitTimeInterval { get; set; }
        internal bool SpeedSwitched { get; set; }
        internal bool DirectionSwitched { get; set; }
        internal int SpeedSwitchCount { get; set; }
        internal int DirectionSwitchCount { get; set; }
        internal int WidthSwitchCount { get; set; }
        internal int HandoffCount { get; set; }
        internal int LaneMask { get; set; }
        internal int EntryLexRank { get; set; }
        internal int ExitLexRank { get; set; }
    }

    private sealed class Candidate
    {
        internal SpineState State { get; set; } = null!;
        internal int OriginalIndex { get; set; }
        internal int StartBatch => State.StartBatchIndex;
        internal int EndBatch => State.BatchIndex;
        internal int EventCount { get; set; }
        internal int LaneCount { get; set; }
    }

    private sealed class Selection
    {
        internal static readonly Selection Empty = new();
        internal int EventCount { get; set; }
        internal int SequenceCount { get; set; }
        internal int BatchCount { get; set; }
        internal int[] CandidateIndexes { get; set; } = Array.Empty<int>();

        internal Selection Add(Candidate candidate)
        {
            var indexes = new int[CandidateIndexes.Length + 1];
            var insert = Array.BinarySearch(CandidateIndexes, candidate.OriginalIndex);
            insert = insert < 0 ? ~insert : insert;
            Array.Copy(CandidateIndexes, 0, indexes, 0, insert);
            indexes[insert] = candidate.OriginalIndex;
            Array.Copy(CandidateIndexes, insert, indexes, insert + 1,
                CandidateIndexes.Length - insert);
            return new Selection
            {
                EventCount = EventCount + candidate.EventCount,
                SequenceCount = SequenceCount + 1,
                BatchCount = BatchCount + candidate.State.Length,
                CandidateIndexes = indexes
            };
        }
    }

    private readonly struct StateKey : IEquatable<StateKey>
    {
        private readonly int _exit;
        private readonly int? _direction;
        private readonly int _run;
        private readonly double? _seconds;
        private readonly BeatPosition? _beats;

        internal StateKey(SpineState state)
        {
            _exit = state.ExitAttackId;
            _direction = state.Direction;
            _run = Math.Min(state.RunSteps, 2);
            _seconds = state.UnitTimeInterval is double seconds
                ? Math.Round(seconds, 9) : null;
            _beats = state.UnitBeatInterval;
        }

        public bool Equals(StateKey other) => _exit == other._exit &&
            _direction == other._direction && _run == other._run &&
            _seconds == other._seconds && _beats == other._beats;
        public override bool Equals(object? obj) => obj is StateKey other && Equals(other);
        public override int GetHashCode() =>
            HashCode.Combine(_exit, _direction, _run, _seconds, _beats);
    }

    private readonly struct LexKey : IComparable<LexKey>, IEquatable<LexKey>
    {
        internal LexKey(int length, int prefix, int current)
        { Length = length; Prefix = prefix; Current = current; }
        internal int Length { get; }
        internal int Prefix { get; }
        internal int Current { get; }
        public int CompareTo(LexKey other)
        {
            var result = Length.CompareTo(other.Length);
            if (result != 0) return result;
            result = Prefix.CompareTo(other.Prefix);
            return result != 0 ? result : Current.CompareTo(other.Current);
        }
        public bool Equals(LexKey other) =>
            Length == other.Length && Prefix == other.Prefix && Current == other.Current;
        public override bool Equals(object? obj) => obj is LexKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Length, Prefix, Current);
    }

    internal static IReadOnlyList<SweepAttack> ButtonAttacks(IReadOnlyList<RadarEvent> events)
    {
        var grouped = new Dictionary<(BeatPosition Beat, int Lane), List<RadarEvent>>();
        foreach (var item in events)
        {
            if (item.Kind is not (RadarEventKind.Tap or RadarEventKind.Hold)) continue;
            if (item.Kind == RadarEventKind.Hold && item.EndBeat - item.StartBeat > ShortHoldMaximum)
                continue;
            if (!int.TryParse(item.Position, out var lane) || lane is < 1 or > 8)
                throw new InvalidOperationException("Button event is missing a valid outer lane.");
            var key = (item.StartBeat, lane);
            if (!grouped.TryGetValue(key, out var declarations))
                grouped[key] = declarations = new List<RadarEvent>();
            declarations.Add(item);
        }

        var output = new List<SweepAttack>();
        foreach (var pair in grouped.OrderBy(pair => pair.Key.Beat).ThenBy(pair => pair.Key.Lane))
        {
            var declarations = pair.Value.OrderBy(item => item.EventId).ToArray();
            var time = declarations[0].StartTimeSeconds;
            if (declarations.Skip(1).Any(item => Math.Abs(item.StartTimeSeconds - time) > 1e-12))
                throw new InvalidOperationException("Same-beat duplicate attacks disagree on chart time.");
            output.Add(new SweepAttack
            {
                Id = output.Count,
                Lane = pair.Key.Lane,
                Beat = pair.Key.Beat,
                TimeSeconds = time,
                EventIds = declarations.Select(item => item.EventId).ToArray(),
                NormalDeclarations = declarations.Count(item => item.IsEx != true),
                ProtectedDeclarations = declarations.Count(item => item.IsEx == true)
            });
        }
        return output;
    }

    internal static IReadOnlyList<SweepSequence> Recognize(
        IReadOnlyList<RadarEvent> events,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var attacks = ButtonAttacks(events);
        if (attacks.Count > MaximumButtonAttacks)
            throw new InvalidOperationException(
                $"Sweep attack budget exceeded ({attacks.Count} > {MaximumButtonAttacks}).");
        var batches = Batches(attacks);
        var candidates = CandidateStates(
            attacks, batches, LongHolds(events), cancellationToken);
        var selected = SelectDisjoint(candidates, cancellationToken);
        return selected.Select(candidate => ToSequence(candidate.State, attacks, batches))
            .OrderBy(item => item, Comparer<SweepSequence>.Create(CompareSequenceOrder))
            .ToArray();
    }

    private static IReadOnlyList<HoldOccupancy> LongHolds(IReadOnlyList<RadarEvent> events) =>
        events.Where(item => item.Kind == RadarEventKind.Hold &&
                            item.EndBeat - item.StartBeat > ShortHoldMaximum)
            .Select(item => new HoldOccupancy
            {
                Lane = int.TryParse(item.Position, out var lane) ? lane :
                    throw new InvalidOperationException("Hold event is missing a valid outer lane."),
                Start = item.StartBeat,
                End = item.EndBeat
            }).ToArray();

    private static IReadOnlyList<AttackBatch> Batches(IReadOnlyList<SweepAttack> attacks) =>
        attacks.GroupBy(item => item.Beat).OrderBy(group => group.Key).Select(group =>
        {
            var members = group.OrderBy(item => item.Lane).ThenBy(item => item.Id).ToArray();
            var time = members[0].TimeSeconds;
            if (members.Skip(1).Any(item => Math.Abs(item.TimeSeconds - time) > 1e-12))
                throw new InvalidOperationException("Same-beat attacks disagree on chart time.");
            return new AttackBatch
            {
                Beat = group.Key,
                Time = time,
                AttackIds = members.Select(item => item.Id).ToArray()
            };
        }).ToArray();

    private static IReadOnlyList<Candidate> CandidateStates(
        IReadOnlyList<SweepAttack> attacks,
        IReadOnlyList<AttackBatch> batches,
        IReadOnlyList<HoldOccupancy> holds,
        CancellationToken cancellationToken)
    {
        var attackPrefix = new int[batches.Count + 1];
        var eventPrefix = new int[batches.Count + 1];
        for (var index = 0; index < batches.Count; index++)
        {
            attackPrefix[index + 1] = attackPrefix[index] + batches[index].AttackIds.Count;
            eventPrefix[index + 1] = eventPrefix[index] + batches[index].AttackIds
                .Sum(id => attacks[id].EventIds.Count);
        }

        var orderedKeys = new List<(int Start, int End)>();
        var candidates = new Dictionary<(int Start, int End), Candidate>();
        var active = new List<SpineState>();
        var visited = 0;
        for (var batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = batches[batchIndex];
            if (batch.AttackIds.Count > 3)
            {
                active.Clear();
                continue;
            }

            var next = batch.AttackIds.Select(attackId => InitialState(
                batchIndex, attackId, attacks[attackId].Lane)).ToList();
            foreach (var state in active)
                foreach (var entry in batch.AttackIds)
                    foreach (var exit in batch.AttackIds)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var advanced = Advance(
                            state, batchIndex, entry, exit, attacks, batches, holds);
                        if (advanced is not null) next.Add(advanced);
                    }

            var keys = new List<StateKey>();
            var deduplicated = new Dictionary<StateKey, SpineState>();
            AssignLexRanks(next, entry: true);
            AssignLexRanks(next, entry: false);
            foreach (var state in next)
            {
                if (++visited > MaximumStates)
                    throw new InvalidOperationException(
                        "Sweep main-spine state budget exceeded; no partial result returned.");
                var key = new StateKey(state);
                if (!deduplicated.TryGetValue(key, out var previous))
                {
                    keys.Add(key);
                    deduplicated[key] = state;
                }
                else if (CompareStateQuality(state, previous) > 0)
                    deduplicated[key] = state;
            }
            active = keys.Select(key => deduplicated[key]).ToList();
            foreach (var state in active.Where(Complete))
            {
                var range = (state.StartBatchIndex, state.BatchIndex);
                var candidate = new Candidate
                {
                    State = state,
                    EventCount = eventPrefix[range.Item2 + 1] - eventPrefix[range.Item1],
                    LaneCount = attackPrefix[range.Item2 + 1] - attackPrefix[range.Item1]
                };
                if (!candidates.TryGetValue(range, out var previous))
                {
                    if (orderedKeys.Count >= MaximumLightweightCandidates)
                        throw new InvalidOperationException(
                            $"Sweep lightweight candidate budget exceeded ({MaximumLightweightCandidates}).");
                    candidate.OriginalIndex = orderedKeys.Count;
                    orderedKeys.Add(range);
                    candidates[range] = candidate;
                }
                else if (CompareCandidateQuality(candidate, previous) > 0)
                {
                    candidate.OriginalIndex = previous.OriginalIndex;
                    candidates[range] = candidate;
                }
            }
        }

        var unique = orderedKeys.Select(key => candidates[key]).ToArray();
        var retained = new bool[unique.Length];
        var furthestEnd = -1;
        foreach (var candidate in unique.OrderBy(item => item.StartBatch)
                     .ThenByDescending(item => item.EndBatch)
                     .ThenBy(item => item.OriginalIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.EndBatch <= furthestEnd) continue;
            retained[candidate.OriginalIndex] = true;
            furthestEnd = candidate.EndBatch;
        }
        return unique.Where(item => retained[item.OriginalIndex]).ToArray();
    }

    private static SpineState InitialState(int batchIndex, int attackId, int lane) => new()
    {
        BatchIndex = batchIndex,
        StartBatchIndex = batchIndex,
        EntryAttackId = attackId,
        ExitAttackId = attackId,
        Length = 1,
        LaneMask = 1 << (lane - 1)
    };

    private static SpineState? Advance(
        SpineState state,
        int batchIndex,
        int entryId,
        int exitId,
        IReadOnlyList<SweepAttack> attacks,
        IReadOnlyList<AttackBatch> batches,
        IReadOnlyList<HoldOccupancy> holds)
    {
        var move = Move(attacks[state.ExitAttackId], attacks[entryId], holds);
        if (move is null) return null;
        var (direction, units) = move.Value;
        var previousBatch = batches[state.BatchIndex];
        var currentBatch = batches[batchIndex];
        var gapBeat = currentBatch.Beat - previousBatch.Beat;
        var gapTime = currentBatch.Time - previousBatch.Time;
        if (gapBeat <= BeatPosition.Zero || gapTime <= 0) return null;
        var unitBeat = Divide(gapBeat, units);
        var unitTime = gapTime / units;

        bool speedSwitch;
        if (state.UnitTimeInterval is null)
        {
            if (unitBeat > BaseMaximumUnit) return null;
            speedSwitch = false;
        }
        else
        {
            var previousTime = state.UnitTimeInterval.Value;
            speedSwitch = !SameSpeed(unitTime, previousTime, SpeedRelativeTolerance);
            var ordinary = unitBeat <= BaseMaximumUnit;
            var continuingEighth = unitBeat == EighthBeat &&
                state.UnitBeatInterval == EighthBeat && !speedSwitch;
            var deceleratingToEighth = unitBeat == EighthBeat && speedSwitch &&
                unitTime > previousTime;
            if (!ordinary && !continuingEighth && !deceleratingToEighth) return null;
        }

        var directionSwitched = false;
        int runSteps, turns;
        if (state.Direction is null)
        {
            runSteps = 1;
            turns = state.TurnCount;
        }
        else if (state.Direction == direction)
        {
            runSteps = state.RunSteps + 1;
            turns = state.TurnCount;
        }
        else
        {
            if (state.RunSteps < 2) return null;
            runSteps = 1;
            turns = state.TurnCount + 1;
            directionSwitched = true;
        }
        return new SpineState
        {
            Previous = state,
            BatchIndex = batchIndex,
            StartBatchIndex = state.StartBatchIndex,
            EntryAttackId = entryId,
            ExitAttackId = exitId,
            Length = state.Length + 1,
            Direction = direction,
            RunSteps = runSteps,
            TurnCount = turns,
            UnitBeatInterval = unitBeat,
            UnitTimeInterval = unitTime,
            SpeedSwitched = speedSwitch,
            DirectionSwitched = directionSwitched,
            SpeedSwitchCount = state.SpeedSwitchCount + (speedSwitch ? 1 : 0),
            DirectionSwitchCount = state.DirectionSwitchCount + (directionSwitched ? 1 : 0),
            WidthSwitchCount = state.WidthSwitchCount +
                (previousBatch.AttackIds.Count != currentBatch.AttackIds.Count ? 1 : 0),
            HandoffCount = state.HandoffCount + (entryId != exitId ? 1 : 0),
            LaneMask = state.LaneMask |
                1 << (attacks[entryId].Lane - 1) |
                1 << (attacks[exitId].Lane - 1)
        };
    }

    private static (int Direction, int Units)? Move(
        SweepAttack left, SweepAttack right, IReadOnlyList<HoldOccupancy> holds)
    {
        var clockwise = Mod(right.Lane - left.Lane, 8);
        var counterclockwise = Mod(left.Lane - right.Lane, 8);
        if (clockwise == 1) return (1, 1);
        if (counterclockwise == 1) return (-1, 1);
        var direction = clockwise == 2 ? 1 : counterclockwise == 2 ? -1 : 0;
        if (direction == 0) return null;
        var skipped = Ring(left.Lane + direction);
        return holds.Any(hold => hold.Lane == skipped && hold.Start <= left.Beat &&
                                 hold.End >= right.Beat)
            ? (direction, 2) : null;
    }

    private static bool Complete(SpineState state) =>
        state.Length >= 3 && state.Direction is not null &&
        (state.TurnCount == 0 || state.RunSteps >= 2) && CountBits(state.LaneMask) >= 3;

    private static SweepSequence ToSequence(
        SpineState state,
        IReadOnlyList<SweepAttack> attacks,
        IReadOnlyList<AttackBatch> batches)
    {
        var chain = new SpineState[state.Length];
        for (var current = state; current is not null; current = current.Previous)
            chain[current.Length - 1] = current;
        var widths = new int[chain.Length];
        var lanes = new IReadOnlyList<int>[chain.Length];
        var times = new double[chain.Length];
        var normal = new int[chain.Length];
        var protectedCounts = new int[chain.Length];
        var unitTimes = new double[chain.Length - 1];
        var mainAttackIds = new int[chain.Length];
        var mainLanes = new int[chain.Length];
        var speedSwitches = new HashSet<int>();
        var directionSwitches = new HashSet<int>();
        var widthSwitches = new HashSet<int>();
        var doubleHandoffs = new HashSet<int>();

        for (var index = 0; index < chain.Length; index++)
        {
            var item = chain[index];
            var batch = batches[item.BatchIndex];
            widths[index] = batch.AttackIds.Count;
            lanes[index] = batch.AttackIds.Select(id => attacks[id].Lane).ToArray();
            times[index] = batch.Time;
            normal[index] = batch.AttackIds.Sum(id => attacks[id].NormalDeclarations);
            protectedCounts[index] = batch.AttackIds.Sum(id => attacks[id].ProtectedDeclarations);
            mainAttackIds[index] = item.EntryAttackId;
            mainLanes[index] = attacks[item.EntryAttackId].Lane;
            if (index == 0) continue;
            unitTimes[index - 1] = item.UnitTimeInterval!.Value;
            if (item.SpeedSwitched) speedSwitches.Add(index);
            if (item.DirectionSwitched) directionSwitches.Add(index - 1);
            if (widths[index - 1] != widths[index]) widthSwitches.Add(index);
            if (item.EntryAttackId != item.ExitAttackId) doubleHandoffs.Add(index);
        }

        return new SweepSequence
        {
            LanesByBatch = lanes,
            Times = times,
            UnitTimeIntervals = unitTimes,
            SpeedSwitches = speedSwitches,
            DirectionSwitches = directionSwitches,
            WidthSwitches = widthSwitches,
            DoubleHandoffs = doubleHandoffs,
            NormalDeclarations = normal,
            ProtectedDeclarations = protectedCounts,
            Widths = widths,
            StartBeat = batches[chain[0].BatchIndex].Beat,
            EndBeat = batches[chain[^1].BatchIndex].Beat,
            AttackCount = widths.Sum(),
            BatchCount = chain.Length,
            MedianIntervalSeconds = Median(unitTimes),
            Strands = new[]
            {
                new SweepStrand
                {
                    AttackIds = mainAttackIds,
                    Lanes = mainLanes,
                    InitialDirection = chain[1].Direction!.Value,
                    FinalDirection = chain[^1].Direction!.Value,
                    TurnCount = state.TurnCount
                }
            }
        };
    }

    private static IReadOnlyList<Candidate> SelectDisjoint(
        IReadOnlyList<Candidate> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0) return Array.Empty<Candidate>();
        var byOriginal = candidates.ToDictionary(item => item.OriginalIndex);
        var components = new List<List<Candidate>>();
        List<Candidate>? current = null;
        var furthestEnd = -1;
        foreach (var candidate in candidates.OrderBy(item => item.StartBatch)
                     .ThenBy(item => item.EndBatch).ThenBy(item => item.OriginalIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current is null || candidate.StartBatch > furthestEnd)
            {
                current = new List<Candidate>();
                components.Add(current);
                furthestEnd = candidate.EndBatch;
            }
            else furthestEnd = Math.Max(furthestEnd, candidate.EndBatch);
            current.Add(candidate);
        }

        var selected = new List<Candidate>();
        var selectionStates = 0;
        foreach (var component in components)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (component.Count == 1)
            {
                selected.Add(component[0]);
                continue;
            }
            var ordered = component.OrderBy(item => item.EndBatch)
                .ThenBy(item => item.StartBatch).ThenBy(item => item.OriginalIndex).ToArray();
            var ends = ordered.Select(item => item.EndBatch).ToArray();
            var best = new Selection[ordered.Length + 1];
            best[0] = Selection.Empty;
            for (var index = 0; index < ordered.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++selectionStates > MaximumSelectionStates)
                    throw new InvalidOperationException(
                        $"Sweep selection budget exceeded ({MaximumSelectionStates}).");
                var previous = LastBefore(ends, ordered[index].StartBatch, index);
                var included = best[previous + 1].Add(ordered[index]);
                var excluded = best[index];
                best[index + 1] = CompareSelection(included, excluded) >= 0
                    ? included : excluded;
            }
            selected.AddRange(best[^1].CandidateIndexes.Select(index => byOriginal[index]));
        }
        return selected;
    }

    private static int CompareStateQuality(SpineState left, SpineState right)
    {
        var result = left.Length.CompareTo(right.Length);
        if (result != 0) return result;
        result = right.SpeedSwitchCount.CompareTo(left.SpeedSwitchCount);
        if (result != 0) return result;
        result = right.TurnCount.CompareTo(left.TurnCount);
        if (result != 0) return result;
        result = right.HandoffCount.CompareTo(left.HandoffCount);
        if (result != 0) return result;
        result = right.EntryLexRank.CompareTo(left.EntryLexRank);
        return result != 0 ? result : right.ExitLexRank.CompareTo(left.ExitLexRank);
    }

    private static int CompareCandidateQuality(Candidate left, Candidate right)
    {
        var result = left.LaneCount.CompareTo(right.LaneCount);
        if (result != 0) return result;
        result = right.State.SpeedSwitchCount.CompareTo(left.State.SpeedSwitchCount);
        if (result != 0) return result;
        result = left.State.Length.CompareTo(right.State.Length);
        if (result != 0) return result;
        result = right.State.DirectionSwitchCount.CompareTo(left.State.DirectionSwitchCount);
        return result != 0 ? result :
            right.State.WidthSwitchCount.CompareTo(left.State.WidthSwitchCount);
    }

    private static int CompareSelection(Selection left, Selection right)
    {
        var result = left.EventCount.CompareTo(right.EventCount);
        if (result != 0) return result;
        result = right.SequenceCount.CompareTo(left.SequenceCount);
        if (result != 0) return result;
        result = left.BatchCount.CompareTo(right.BatchCount);
        return result != 0 ? result : -LexicographicCompare(
            left.CandidateIndexes, right.CandidateIndexes);
    }

    private static void AssignLexRanks(IReadOnlyList<SpineState> states, bool entry)
    {
        var keys = ArrayPool<LexKey>.Shared.Rent(states.Count);
        try
        {
            for (var index = 0; index < states.Count; index++)
                keys[index] = LexKeyFor(states[index], entry);
            Array.Sort(keys, 0, states.Count);
            var uniqueCount = 0;
            for (var index = 0; index < states.Count; index++)
                if (uniqueCount == 0 || !keys[index].Equals(keys[uniqueCount - 1]))
                    keys[uniqueCount++] = keys[index];
            foreach (var item in states)
            {
                var rank = Array.BinarySearch(keys, 0, uniqueCount, LexKeyFor(item, entry));
                if (entry) item.EntryLexRank = rank;
                else item.ExitLexRank = rank;
            }
        }
        finally
        {
            ArrayPool<LexKey>.Shared.Return(keys);
        }
    }

    private static LexKey LexKeyFor(SpineState item, bool entry) => new(
        item.Length,
        item.Previous is null ? 0 :
            entry ? item.Previous.EntryLexRank : item.Previous.ExitLexRank,
        entry ? item.EntryAttackId : item.ExitAttackId);

    private static int CompareSequenceOrder(SweepSequence left, SweepSequence right)
    {
        var result = left.StartBeat.CompareTo(right.StartBeat);
        if (result != 0) return result;
        result = left.EndBeat.CompareTo(right.EndBeat);
        return result != 0 ? result : CompareNested(left.LanesByBatch, right.LanesByBatch);
    }

    private static int LastBefore(int[] sortedEnds, int start, int exclusiveEnd)
    {
        var low = 0;
        var high = exclusiveEnd;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (sortedEnds[middle] < start) low = middle + 1;
            else high = middle;
        }
        return low - 1;
    }

    private static double Median(double[] values)
    {
        var ordered = (double[])values.Clone();
        Array.Sort(ordered);
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 1
            ? ordered[middle]
            : (ordered[middle - 1] + ordered[middle]) / 2;
    }

    private static int CountBits(int value)
    {
        var count = 0;
        while (value != 0) { value &= value - 1; count++; }
        return count;
    }

    private static BeatPosition Divide(BeatPosition value, int divisor) =>
        new(value.Numerator, checked(value.Denominator * divisor));
    private static int Ring(int lane) => Mod(lane - 1, 8) + 1;
    private static int Mod(int value, int divisor) => (value % divisor + divisor) % divisor;
    internal static int CircularDistance(int left, int right)
    {
        var distance = Math.Abs(left - right);
        return Math.Min(distance, 8 - distance);
    }
    private static bool SameSpeed(double left, double right, double tolerance) =>
        Math.Abs(left - right) <= Math.Max(TimeTolerance,
            tolerance * Math.Max(Math.Abs(left), Math.Abs(right)));

    private static int LexicographicCompare<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
        where T : IComparable<T>
    {
        for (var index = 0; index < Math.Min(left.Count, right.Count); index++)
        {
            var result = left[index].CompareTo(right[index]);
            if (result != 0) return result;
        }
        return left.Count.CompareTo(right.Count);
    }

    private static int CompareNested(
        IReadOnlyList<IReadOnlyList<int>> left,
        IReadOnlyList<IReadOnlyList<int>> right)
    {
        for (var index = 0; index < Math.Min(left.Count, right.Count); index++)
        {
            var result = LexicographicCompare(left[index], right[index]);
            if (result != 0) return result;
        }
        return left.Count.CompareTo(right.Count);
    }
}
