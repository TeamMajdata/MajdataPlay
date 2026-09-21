using System;
using System.Globalization;

#nullable enable

namespace SimaiRadar.Core;

/// <summary>A reduced rational position on the global quarter-note beat axis.</summary>
public readonly struct BeatPosition : IComparable<BeatPosition>, IEquatable<BeatPosition>
{
    public long Numerator { get; }
    public long Denominator { get; }

    public BeatPosition(long numerator, long denominator = 1)
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

    public static BeatPosition Zero => new(0);

    /// <summary>
    /// Bins a floating-point beat distance to a rational subdivision. A sufficiently
    /// close simple fraction wins; otherwise the closest fraction within the allowed
    /// denominator range is returned, so ordinary floating-point noise never rejects
    /// an otherwise usable chart.
    /// </summary>
    public static BeatPosition SnapFromDouble(
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
    public double ToDouble() => (double)Numerator / Denominator;
    public override string ToString() => Denominator == 1
        ? Numerator.ToString(CultureInfo.InvariantCulture)
        : $"{Numerator.ToString(CultureInfo.InvariantCulture)}/{Denominator.ToString(CultureInfo.InvariantCulture)}";

    public static BeatPosition operator +(BeatPosition left, BeatPosition right) =>
        new(checked(left.Numerator * right.Denominator + right.Numerator * left.Denominator),
            checked(left.Denominator * right.Denominator));

    public static bool operator ==(BeatPosition left, BeatPosition right) => left.Equals(right);
    public static bool operator !=(BeatPosition left, BeatPosition right) => !left.Equals(right);

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
