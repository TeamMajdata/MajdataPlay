using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using SimaiRadar.Core;

#nullable enable

namespace SimaiRadar.Analysis.Features;

/// <summary>
/// Recognizes maximal one-spine, variable-width button sweeps. This is kept
/// separate from scoring so recognition regressions can be reviewed directly.
/// </summary>
internal static class SweepRecognizer
{
    internal const int MaximumStates = 100_000;
    internal const int MaximumButtonAttacks = 20_000;
    internal const long MaximumSpineHistoryUnits = 2_000_000;
    internal const long MaximumCandidateHistoryUnits = 100_000;
    internal const int MaximumCandidates = 512;
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
        internal IReadOnlyList<int> BatchIndexes { get; set; } = Array.Empty<int>();
        internal IReadOnlyList<int> EntryAttackIds { get; set; } = Array.Empty<int>();
        internal IReadOnlyList<int> ExitAttackIds { get; set; } = Array.Empty<int>();
        internal int? Direction { get; set; }
        internal int RunSteps { get; set; }
        internal int TurnCount { get; set; }
        internal IReadOnlyList<BeatPosition> UnitBeatIntervals { get; set; } =
            Array.Empty<BeatPosition>();
        internal IReadOnlyList<double> UnitTimeIntervals { get; set; } = Array.Empty<double>();
        internal IReadOnlyList<bool> SpeedSwitches { get; set; } = Array.Empty<bool>();
        internal IReadOnlyList<bool> DirectionSwitches { get; set; } = Array.Empty<bool>();
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
            _exit = state.ExitAttackIds[^1];
            _direction = state.Direction;
            _run = Math.Min(state.RunSteps, 2);
            _seconds = state.UnitTimeIntervals.Count == 0
                ? null : Math.Round(state.UnitTimeIntervals[^1], 9);
            _beats = state.UnitBeatIntervals.Count == 0 ? null : state.UnitBeatIntervals[^1];
        }

        public bool Equals(StateKey other) => _exit == other._exit &&
            _direction == other._direction && _run == other._run &&
            _seconds == other._seconds && _beats == other._beats;
        public override bool Equals(object? obj) => obj is StateKey other && Equals(other);
        public override int GetHashCode() =>
            HashCode.Combine(_exit, _direction, _run, _seconds, _beats);
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
        var candidates = CandidateSequences(
            attacks, batches, LongHolds(events), cancellationToken);
        return SelectDisjoint(candidates, cancellationToken);
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

    private static IReadOnlyList<SweepSequence> CandidateSequences(
        IReadOnlyList<SweepAttack> attacks,
        IReadOnlyList<AttackBatch> batches,
        IReadOnlyList<HoldOccupancy> holds,
        CancellationToken cancellationToken)
    {
        var candidates = new List<SweepSequence>();
        var active = new List<SpineState>();
        var visited = 0;
        long historyUnits = 0;
        long candidateHistoryUnits = 0;

        for (var batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = batches[batchIndex];
            if (batch.AttackIds.Count > 3)
            {
                active.Clear();
                continue;
            }

            var next = batch.AttackIds.Select(attackId => new SpineState
            {
                BatchIndexes = new[] { batchIndex },
                EntryAttackIds = new[] { attackId },
                ExitAttackIds = new[] { attackId }
            }).ToList();
            foreach (var state in active)
                foreach (var entry in batch.AttackIds)
                    foreach (var exit in batch.AttackIds)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var advanced = Advance(
                            state, batchIndex, entry, exit, attacks, batches, holds);
                        if (advanced is null) continue;
                        historyUnits += advanced.EntryAttackIds.Count;
                        if (historyUnits > MaximumSpineHistoryUnits)
                            throw new InvalidOperationException(
                                $"Sweep history budget exceeded ({MaximumSpineHistoryUnits}).");
                        next.Add(advanced);
                    }

            var keys = new List<StateKey>();
            var deduplicated = new Dictionary<StateKey, SpineState>();
            foreach (var state in next)
            {
                if (++visited > MaximumStates)
                    throw new InvalidOperationException(
                        "Sweep main-spine candidate limit exceeded; no partial result returned.");
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
            foreach (var state in active.Where(state => Complete(state, attacks)))
            {
                candidateHistoryUnits += state.EntryAttackIds.Count;
                if (candidateHistoryUnits > MaximumCandidateHistoryUnits)
                    throw new InvalidOperationException(
                        $"Sweep candidate history budget exceeded ({MaximumCandidateHistoryUnits}).");
                if (candidates.Count >= MaximumCandidates)
                    throw new InvalidOperationException(
                        $"Sweep candidate budget exceeded ({MaximumCandidates}).");
                candidates.Add(ToSequence(state, attacks, batches));
            }
        }

        var uniqueOrder = new List<string>();
        var unique = new Dictionary<string, SweepSequence>();
        foreach (var candidate in candidates)
        {
            var key = EventSetKey(candidate);
            if (!unique.TryGetValue(key, out var previous))
            {
                uniqueOrder.Add(key);
                unique[key] = candidate;
            }
            else if (CompareCandidateQuality(candidate, previous) > 0)
                unique[key] = candidate;
        }
        var items = uniqueOrder.Select((key, index) =>
            (Index: index, Events: EventSet(unique[key]), Item: unique[key])).ToArray();
        var retained = new List<(int Index, HashSet<int> Events, SweepSequence Item)>();
        var retainedByEvent = new Dictionary<int, List<int>>();
        foreach (var item in items.OrderByDescending(item => item.Events.Count)
                     .ThenBy(item => item.Index))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var probeEvent = item.Events.First();
            var contained = retainedByEvent.TryGetValue(probeEvent, out var possible) &&
                possible.Any(index => retained[index].Events.Count > item.Events.Count &&
                                      item.Events.IsSubsetOf(retained[index].Events));
            if (contained) continue;
            var retainedIndex = retained.Count;
            retained.Add(item);
            foreach (var eventId in item.Events)
            {
                if (!retainedByEvent.TryGetValue(eventId, out var indexes))
                    retainedByEvent[eventId] = indexes = new List<int>();
                indexes.Add(retainedIndex);
            }
        }
        return retained.OrderBy(item => item.Index).Select(item => item.Item).ToArray();
    }

    private static SpineState? Advance(
        SpineState state,
        int batchIndex,
        int entryId,
        int exitId,
        IReadOnlyList<SweepAttack> attacks,
        IReadOnlyList<AttackBatch> batches,
        IReadOnlyList<HoldOccupancy> holds)
    {
        var move = Move(attacks[state.ExitAttackIds[^1]], attacks[entryId], holds);
        if (move is null) return null;
        var (direction, units) = move.Value;
        var previousBatch = batches[state.BatchIndexes[^1]];
        var currentBatch = batches[batchIndex];
        var gapBeat = currentBatch.Beat - previousBatch.Beat;
        var gapTime = currentBatch.Time - previousBatch.Time;
        if (gapBeat <= BeatPosition.Zero || gapTime <= 0) return null;
        var unitBeat = Divide(gapBeat, units);
        var unitTime = gapTime / units;

        bool speedSwitch;
        if (state.UnitTimeIntervals.Count == 0)
        {
            if (unitBeat > BaseMaximumUnit) return null;
            speedSwitch = false;
        }
        else
        {
            var previousTime = state.UnitTimeIntervals[^1];
            speedSwitch = !SameSpeed(unitTime, previousTime, SpeedRelativeTolerance);
            var ordinary = unitBeat <= BaseMaximumUnit;
            var continuingEighth = unitBeat == EighthBeat &&
                state.UnitBeatIntervals[^1] == EighthBeat && !speedSwitch;
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
            BatchIndexes = Append(state.BatchIndexes, batchIndex),
            EntryAttackIds = Append(state.EntryAttackIds, entryId),
            ExitAttackIds = Append(state.ExitAttackIds, exitId),
            Direction = direction,
            RunSteps = runSteps,
            TurnCount = turns,
            UnitBeatIntervals = Append(state.UnitBeatIntervals, unitBeat),
            UnitTimeIntervals = Append(state.UnitTimeIntervals, unitTime),
            SpeedSwitches = Append(state.SpeedSwitches, speedSwitch),
            DirectionSwitches = Append(state.DirectionSwitches, directionSwitched)
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

    private static bool Complete(SpineState state, IReadOnlyList<SweepAttack> attacks) =>
        state.EntryAttackIds.Count >= 3 && state.Direction is not null &&
        (state.TurnCount == 0 || state.RunSteps >= 2) &&
        state.EntryAttackIds.Concat(state.ExitAttackIds)
            .Select(id => attacks[id].Lane).Distinct().Count() >= 3;

    private static SweepSequence ToSequence(
        SpineState state,
        IReadOnlyList<SweepAttack> attacks,
        IReadOnlyList<AttackBatch> batches)
    {
        var included = state.BatchIndexes.Select(index => batches[index].AttackIds).ToArray();
        var widths = included.Select(batch => batch.Count).ToArray();
        var main = state.EntryAttackIds.Select(id => attacks[id]).ToArray();
        var directions = state.ExitAttackIds.Take(state.ExitAttackIds.Count - 1)
            .Zip(state.EntryAttackIds.Skip(1), (left, right) =>
                Mod(attacks[right].Lane - attacks[left].Lane, 8) is 1 or 2 ? 1 : -1)
            .ToArray();
        return new SweepSequence
        {
            LanesByBatch = included.Select(batch =>
                (IReadOnlyList<int>)batch.Select(id => attacks[id].Lane).ToArray()).ToArray(),
            AttackIdsByBatch = included,
            EventIdsByBatch = included.Select(batch =>
                (IReadOnlyList<int>)batch.SelectMany(id => attacks[id].EventIds).ToArray()).ToArray(),
            Beats = state.BatchIndexes.Select(index => batches[index].Beat).ToArray(),
            Times = state.BatchIndexes.Select(index => batches[index].Time).ToArray(),
            UnitBeatIntervals = state.UnitBeatIntervals,
            UnitTimeIntervals = state.UnitTimeIntervals,
            SpeedSwitches = state.SpeedSwitches.Select((changed, index) => (changed, index))
                .Where(item => item.changed).Select(item => item.index + 1).ToHashSet(),
            DirectionSwitches = state.DirectionSwitches.Select((changed, index) => (changed, index))
                .Where(item => item.changed).Select(item => item.index).ToHashSet(),
            WidthSwitches = widths.Zip(widths.Skip(1), (left, right) => (left, right))
                .Select((pair, index) => (pair, index: index + 1))
                .Where(item => item.pair.left != item.pair.right)
                .Select(item => item.index).ToHashSet(),
            DoubleHandoffs = state.EntryAttackIds.Zip(state.ExitAttackIds,
                    (entry, exit) => entry != exit)
                .Select((changed, index) => (changed, index)).Where(item => item.changed)
                .Select(item => item.index).ToHashSet(),
            NormalDeclarations = included.Select(batch =>
                batch.Sum(id => attacks[id].NormalDeclarations)).ToArray(),
            ProtectedDeclarations = included.Select(batch =>
                batch.Sum(id => attacks[id].ProtectedDeclarations)).ToArray(),
            Strands = new[]
            {
                new SweepStrand
                {
                    AttackIds = state.EntryAttackIds,
                    Lanes = main.Select(item => item.Lane).ToArray(),
                    InitialDirection = directions[0],
                    FinalDirection = directions[^1],
                    TurnCount = state.TurnCount
                }
            }
        };
    }

    private static IReadOnlyList<SweepSequence> SelectDisjoint(
        IReadOnlyList<SweepSequence> sequences,
        CancellationToken cancellationToken)
    {
        if (sequences.Count == 0) return Array.Empty<SweepSequence>();
        var eventSets = sequences.Select(EventSet).ToArray();
        var adjacency = Enumerable.Range(0, sequences.Count)
            .Select(_ => new HashSet<int>()).ToArray();
        var byEvent = new Dictionary<int, List<int>>();
        for (var index = 0; index < eventSets.Length; index++)
            foreach (var eventId in eventSets[index])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!byEvent.TryGetValue(eventId, out var previous))
                    byEvent[eventId] = previous = new List<int>();
                foreach (var other in previous)
                {
                    adjacency[index].Add(other);
                    adjacency[other].Add(index);
                }
                previous.Add(index);
            }

        var components = new List<int[]>();
        var seen = new bool[sequences.Count];
        for (var start = 0; start < sequences.Count; start++)
        {
            if (seen[start]) continue;
            var members = new List<int>();
            var pending = new Stack<int>();
            pending.Push(start);
            seen[start] = true;
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = pending.Pop();
                members.Add(current);
                foreach (var neighbor in adjacency[current])
                    if (!seen[neighbor]) { seen[neighbor] = true; pending.Push(neighbor); }
            }
            components.Add(members.OrderBy(index => index).ToArray());
        }

        var states = 0;
        int[] SolveComponent(int[] component)
        {
            if (component.Length == 1) return component;
            var localByGlobal = component.Select((global, local) => (global, local))
                .ToDictionary(item => item.global, item => item.local);
            var conflicts = new BigInteger[component.Length];
            for (var local = 0; local < component.Length; local++)
            {
                conflicts[local] = BigInteger.One << local;
                foreach (var neighbor in adjacency[component[local]])
                    conflicts[local] |= BigInteger.One << localByGlobal[neighbor];
            }
            var full = (BigInteger.One << component.Length) - 1;
            var memo = new Dictionary<BigInteger, int[]> { [BigInteger.Zero] = Array.Empty<int>() };
            var stack = new Stack<(BigInteger Mask, bool Expanded)>();
            stack.Push((full, false));
            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (mask, expanded) = stack.Pop();
                if (memo.ContainsKey(mask)) continue;
                var pivot = LowestBitIndex(mask);
                var includedMask = mask & ~conflicts[pivot];
                var excludedMask = mask ^ (BigInteger.One << pivot);
                if (!expanded)
                {
                    if (++states > MaximumStates)
                        throw new InvalidOperationException(
                            "Sweep family selection budget exceeded; no partial result returned.");
                    stack.Push((mask, true));
                    if (!memo.ContainsKey(excludedMask)) stack.Push((excludedMask, false));
                    if (!memo.ContainsKey(includedMask)) stack.Push((includedMask, false));
                    continue;
                }
                var included = new[] { component[pivot] }.Concat(memo[includedMask]).ToArray();
                var excluded = memo[excludedMask];
                var comparison = CompareSelection(included, excluded, eventSets, sequences);
                memo[mask] = comparison > 0 ? included : comparison < 0 ? excluded :
                    (LexicographicCompare(included, excluded) <= 0 ? included : excluded);
            }
            return memo[full];
        }

        var indexes = components.SelectMany(SolveComponent).OrderBy(index => index).ToArray();
        return indexes.Select(index => sequences[index]).OrderBy(item => item,
            Comparer<SweepSequence>.Create(CompareSequenceOrder)).ToArray();
    }

    private static int CompareStateQuality(SpineState left, SpineState right)
    {
        var result = left.EntryAttackIds.Count.CompareTo(right.EntryAttackIds.Count);
        if (result != 0) return result;
        result = right.SpeedSwitches.Count(value => value).CompareTo(left.SpeedSwitches.Count(value => value));
        if (result != 0) return result;
        result = right.TurnCount.CompareTo(left.TurnCount);
        if (result != 0) return result;
        var leftHandoffs = left.EntryAttackIds.Zip(left.ExitAttackIds, (a, b) => a != b).Count(x => x);
        var rightHandoffs = right.EntryAttackIds.Zip(right.ExitAttackIds, (a, b) => a != b).Count(x => x);
        result = rightHandoffs.CompareTo(leftHandoffs);
        if (result != 0) return result;
        result = -LexicographicCompare(left.EntryAttackIds, right.EntryAttackIds);
        return result != 0 ? result : -LexicographicCompare(left.ExitAttackIds, right.ExitAttackIds);
    }

    private static int CompareCandidateQuality(SweepSequence left, SweepSequence right)
    {
        var result = left.LanesByBatch.Sum(batch => batch.Count)
            .CompareTo(right.LanesByBatch.Sum(batch => batch.Count));
        if (result != 0) return result;
        result = right.SpeedSwitches.Count.CompareTo(left.SpeedSwitches.Count);
        if (result != 0) return result;
        result = left.Strands.Max(strand => strand.AttackIds.Count)
            .CompareTo(right.Strands.Max(strand => strand.AttackIds.Count));
        if (result != 0) return result;
        result = right.DirectionSwitches.Count.CompareTo(left.DirectionSwitches.Count);
        return result != 0 ? result : right.WidthSwitches.Count.CompareTo(left.WidthSwitches.Count);
    }

    private static int CompareSelection(
        IReadOnlyList<int> left, IReadOnlyList<int> right,
        IReadOnlyList<HashSet<int>> eventSets, IReadOnlyList<SweepSequence> sequences)
    {
        var result = left.Sum(index => eventSets[index].Count)
            .CompareTo(right.Sum(index => eventSets[index].Count));
        if (result != 0) return result;
        result = right.Count.CompareTo(left.Count);
        return result != 0 ? result : left.Sum(index => sequences[index].Beats.Count)
            .CompareTo(right.Sum(index => sequences[index].Beats.Count));
    }

    private static int CompareSequenceOrder(SweepSequence left, SweepSequence right)
    {
        var result = left.StartBeat.CompareTo(right.StartBeat);
        if (result != 0) return result;
        result = left.EndBeat.CompareTo(right.EndBeat);
        if (result != 0) return result;
        return CompareNested(left.LanesByBatch, right.LanesByBatch);
    }

    private static HashSet<int> EventSet(SweepSequence sequence) =>
        sequence.EventIdsByBatch.SelectMany(batch => batch).ToHashSet();
    private static string EventSetKey(SweepSequence sequence) =>
        string.Join(",", EventSet(sequence).OrderBy(value => value));
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

    private static T[] Append<T>(IReadOnlyList<T> source, T value) =>
        source.Concat(new[] { value }).ToArray();

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

    private static int LowestBitIndex(BigInteger value)
    {
        var index = 0;
        while ((value & BigInteger.One).IsZero) { value >>= 1; index++; }
        return index;
    }
}
