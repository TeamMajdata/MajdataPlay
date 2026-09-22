using System;
using System.Collections.Generic;
using System.Globalization;

#nullable enable

namespace MajdataPlay.Utils.ChartRadar;

internal enum RadarEventKind
{
    Tap,
    Hold,
    Touch,
    TouchHold,
    Slide,
    Timing
}

internal sealed class SlidePathSegment
{
    internal string Shape { get; set; } = string.Empty;
    internal int StartPosition { get; set; }
    internal int? ViaPosition { get; set; }
    internal int EndPosition { get; set; }
    internal int BarCount { get; set; }
    internal double StartTimeSeconds { get; set; }
    internal double EndTimeSeconds { get; set; }
    internal string RawSegment { get; set; } = string.Empty;
}

internal sealed class RadarEvent
{
    internal int EventId { get; set; }
    internal RadarEventKind Kind { get; set; }
    internal bool? IsSlideHead { get; set; }
    internal double? SlideDeclareTimeSeconds { get; set; }
    internal BeatPosition? SlideDeclareBeat { get; set; }
    internal double StartTimeSeconds { get; set; }
    internal double EndTimeSeconds { get; set; }
    internal BeatPosition StartBeat { get; set; }
    internal BeatPosition EndBeat { get; set; }
    internal double? Bpm { get; set; }
    internal string? Position { get; set; }
    internal int? HeadEventId { get; set; }
    internal int? SlideGroupId { get; set; }
    internal IReadOnlyList<SlidePathSegment>? SlidePath { get; set; }
    internal bool? IsBreak { get; set; }
    internal bool? IsEx { get; set; }
    internal bool? IsMine { get; set; }
    internal IReadOnlyDictionary<string, bool>? Flags { get; set; }
    internal string RawToken { get; set; } = string.Empty;
    internal int DeclarationOrder { get; set; }
}

internal sealed class RadarChartInput
{
    internal IReadOnlyList<RadarEvent> Events { get; set; } = Array.Empty<RadarEvent>();
    internal double ChartEndTimeSeconds { get; set; }
    internal double? LastEventEndTimeSeconds { get; set; }
}

/// <summary>A reduced rational position on the global quarter-note beat axis.</summary>
internal readonly struct BeatPosition : IComparable<BeatPosition>, IEquatable<BeatPosition>
{
    internal long Numerator { get; }
    internal long Denominator { get; }

    internal BeatPosition(long numerator, long denominator = 1)
    {
        if (denominator == 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var divisor = GreatestCommonDivisor(Math.Abs(numerator), denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    internal static BeatPosition Zero => new(0);

    /// <summary>
    /// Bins a floating-point beat distance to a rational subdivision. A sufficiently
    /// close simple fraction wins; otherwise the closest fraction within the allowed
    /// denominator range is returned.
    /// </summary>
    internal static BeatPosition SnapFromDouble(
        double value,
        int maxDenominator,
        double tolerance)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        if (maxDenominator < 1) throw new ArgumentOutOfRangeException(nameof(maxDenominator));
        if (double.IsNaN(tolerance) || double.IsInfinity(tolerance) || tolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance));

        long bestNumerator = 0;
        var bestDenominator = 1;
        var bestError = double.PositiveInfinity;
        for (var denominator = 1; denominator <= maxDenominator; denominator++)
        {
            var numerator = checked((long)Math.Round(value * denominator));
            var candidate = (double)numerator / denominator;
            var error = Math.Abs(candidate - value);
            if (error <= tolerance)
                return new BeatPosition(numerator, denominator);
            if (error < bestError)
            {
                bestNumerator = numerator;
                bestDenominator = denominator;
                bestError = error;
            }
        }
        return new BeatPosition(bestNumerator, bestDenominator);
    }

    public int CompareTo(BeatPosition other) =>
        checked(Numerator * other.Denominator).CompareTo(checked(other.Numerator * Denominator));

    public bool Equals(BeatPosition other) =>
        Numerator == other.Numerator && Denominator == other.Denominator;

    public override bool Equals(object? obj) => obj is BeatPosition other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);
    internal double ToDouble() => (double)Numerator / Denominator;
    public override string ToString() => Denominator == 1
        ? Numerator.ToString(CultureInfo.InvariantCulture)
        : $"{Numerator.ToString(CultureInfo.InvariantCulture)}/{Denominator.ToString(CultureInfo.InvariantCulture)}";

    public static BeatPosition operator +(BeatPosition left, BeatPosition right) =>
        new(checked(left.Numerator * right.Denominator + right.Numerator * left.Denominator),
            checked(left.Denominator * right.Denominator));

    public static BeatPosition operator -(BeatPosition left, BeatPosition right) =>
        new(checked(left.Numerator * right.Denominator - right.Numerator * left.Denominator),
            checked(left.Denominator * right.Denominator));

    public static bool operator ==(BeatPosition left, BeatPosition right) => left.Equals(right);
    public static bool operator !=(BeatPosition left, BeatPosition right) => !left.Equals(right);
    public static bool operator <(BeatPosition left, BeatPosition right) => left.CompareTo(right) < 0;
    public static bool operator <=(BeatPosition left, BeatPosition right) => left.CompareTo(right) <= 0;
    public static bool operator >(BeatPosition left, BeatPosition right) => left.CompareTo(right) > 0;
    public static bool operator >=(BeatPosition left, BeatPosition right) => left.CompareTo(right) >= 0;

    private static long GreatestCommonDivisor(long left, long right)
    {
        while (right != 0)
        {
            var remainder = left % right;
            left = right;
            right = remainder;
        }
        return left == 0 ? 1 : left;
    }
}
