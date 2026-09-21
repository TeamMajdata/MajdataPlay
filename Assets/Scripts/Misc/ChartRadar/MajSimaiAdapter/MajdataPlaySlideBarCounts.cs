#nullable enable

namespace SimaiRadar.MajSimaiAdapter;

/// <summary>
/// MajdataPlay's standard Slide prefab lengths. These are gameplay length units
/// used to divide connected-Slide time, not physical distance or note counts.
/// </summary>
internal static class MajdataPlaySlideBarCounts
{
    internal static int Resolve(
        string shape,
        int startPosition,
        int? viaPosition,
        int endPosition)
    {
        var relativeEnd = PositiveModulo(endPosition - startPosition, 8) + 1;
        var prefab = shape switch
        {
            "-" => $"line{relativeEnd}",
            ">" => $"circle{(startPosition is 7 or 8 or 1 or 2 ? relativeEnd : Mirror(relativeEnd))}",
            "<" => $"circle{(startPosition is 3 or 4 or 5 or 6 ? relativeEnd : Mirror(relativeEnd))}",
            "^" => $"circle{(relativeEnd < 5 ? relativeEnd : Mirror(relativeEnd))}",
            "v" => $"v{relativeEnd}",
            "p" => $"pq{relativeEnd}",
            "q" => $"pq{Mirror(relativeEnd)}",
            "pp" => $"ppqq{relativeEnd}",
            "qq" => $"ppqq{Mirror(relativeEnd)}",
            "s" or "z" => "s",
            "w" => "wifi",
            "V" => ResolveLargeV(startPosition, viaPosition, relativeEnd),
            _ => throw new MajSimaiAdaptationException($"Unsupported Slide shape: {shape}")
        };
        return prefab switch
        {
            "line3" => 14,
            "line4" => 19,
            "line5" => 20,
            "line6" => 19,
            "line7" => 14,
            "circle1" => 64,
            "circle2" => 8,
            "circle3" => 16,
            "circle4" => 24,
            "circle5" => 32,
            "circle6" => 40,
            "circle7" => 48,
            "circle8" => 56,
            // Keep the established analysis reference: the dedicated Star_V_1
            // prefab has 21 children and describes the intended 1v1 path.
            "v1" => 21,
            "v2" or "v3" or "v4" or "v6" or "v7" or "v8" => 20,
            "ppqq1" => 36,
            "ppqq2" => 29,
            "ppqq3" => 23,
            "ppqq4" or "ppqq5" => 50,
            "ppqq6" => 49,
            "ppqq7" => 47,
            "ppqq8" => 42,
            "pq1" => 34,
            "pq2" => 31,
            "pq3" => 28,
            "pq4" => 25,
            "pq5" => 22,
            "pq6" => 43,
            "pq7" => 41,
            "pq8" => 37,
            "s" => 31,
            "wifi" => 12,
            "L2" => 33,
            "L3" => 35,
            "L4" => 33,
            "L5" => 29,
            _ => throw new MajSimaiAdaptationException(
                $"No standard MajdataPlay bar count for {startPosition}{shape}{viaPosition}{endPosition} ({prefab}).")
        };
    }

    private static string ResolveLargeV(int start, int? via, int relativeEnd)
    {
        if (via is null)
            throw new MajSimaiAdaptationException("A V Slide requires a via position.");
        var turn = PositiveModulo(via.Value - start, 8);
        return turn switch
        {
            6 => $"L{relativeEnd}",
            2 => $"L{Mirror(relativeEnd)}",
            _ => throw new MajSimaiAdaptationException("A V Slide has an invalid via position.")
        };
    }

    private static int Mirror(int position) => position == 1 ? 1 : 10 - position;
    private static int PositiveModulo(int value, int modulus) => (value % modulus + modulus) % modulus;
}
