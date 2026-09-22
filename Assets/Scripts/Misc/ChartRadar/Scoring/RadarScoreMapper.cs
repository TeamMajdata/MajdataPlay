using System;
using System.Collections.Generic;
using SimaiRadar.Analysis;

#nullable enable

namespace SimaiRadar.Scoring;

public sealed class RadarScoreResult
{
    public IReadOnlyDictionary<string, double> Values { get; set; } =
        new Dictionary<string, double>();
    public string MappingVersion { get; set; } = string.Empty;
}

/// <summary>Frozen visualizer mapping applied after raw analysis and regression.</summary>
public sealed class RadarScoreMapper
{
    public const string MappingVersion = "mapping-profile-2026-09-16T15-44-08-825Z";
    private const double MaximumScore = 250;

    // Fixed calibration for the seven raw dimensions. FittedConstant is appended
    // unchanged. UI code may select axes only after this mapping step.
    private static readonly IReadOnlyDictionary<string, Parameters> Dimensions =
        new Dictionary<string, Parameters>
        {
            [RadarFeatureNames.Note] = new(
                5.726436880674509, 6.967085905412081, 7.8980899226483094,
                9.726945003226584, 11.24420363123459),
            [RadarFeatureNames.Peak] = new(
                8.190000000000001, 10.08, 11.746367029516382,
                16.128373810879122, 22.568482830208243),
            [RadarFeatureNames.Sweep] = new(
                2.118693243901749, 6.631886165225393, 11.445833613896523,
                25.019131254128578, 45.612758813473654),
            [RadarFeatureNames.SlideTricky] = new(
                3.624219535832218, 5.025615522059357, 6.525702339111531,
                10.55540386715839, 16.901251108712703),
            [RadarFeatureNames.SlideSequence] = new(
                1.5833333333333566, 2.353449955917629, 2.880594338282008,
                4.18329204883843, 5.959960226275402),
            [RadarFeatureNames.Jack] = new(
                5.2091929612604035, 10.007454228271365, 16.321518687821236,
                55.493906948148926, 146.65442246453955),
            [RadarFeatureNames.SlideCumulate] = new(
                0.2697357566988737, 0.4447430791696216, 0.556824272562115,
                0.9616680639691454, 1.6578864692145834)
        };

    public RadarScoreResult Map(RadarAnalysisResult analysis, double fittedConstant)
    {
        if (!analysis.IsSuccess)
            throw new ArgumentException("Scoring requires all seven raw features.", nameof(analysis));
        if (double.IsNaN(fittedConstant) || double.IsInfinity(fittedConstant))
            throw new ArgumentOutOfRangeException(nameof(fittedConstant));

        var values = new Dictionary<string, double>();
        foreach (var name in RadarFeatureNames.ModelInputOrder)
            values[name] = MapValue(analysis.Features[name].Value!.Value, Dimensions[name]);
        values[RadarFeatureNames.FittedConstant] = fittedConstant;
        return new RadarScoreResult { Values = values, MappingVersion = MappingVersion };
    }

    private static double MapValue(double value, Parameters parameters)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        if (value <= 0) return 0;

        var raw = parameters.Anchors;
        var score = new[] { 50.0, 100.0, 150.0, 200.0 };
        var lowerRaw = 0.0;
        var lowerScore = 0.0;
        for (var index = 0; index < raw.Length; index++)
        {
            if (value <= raw[index])
                return lowerScore + (score[index] - lowerScore) *
                    (value - lowerRaw) / (raw[index] - lowerRaw);
            lowerRaw = raw[index];
            lowerScore = score[index];
        }
        if (value <= parameters.T4Maximum) return 200;
        var initialSlope = 50 / (raw[3] - raw[2]);
        var headroom = MaximumScore - 200;
        return 200 + headroom *
            (1 - Math.Exp(-initialSlope * (value - parameters.T4Maximum) / headroom));
    }

    private sealed class Parameters
    {
        internal Parameters(double t1, double t2, double t3, double t4, double t4Maximum)
        {
            Anchors = new[] { t1, t2, t3, t4 };
            T4Maximum = t4Maximum;
        }

        internal double[] Anchors { get; }
        internal double T4Maximum { get; }
    }
}
