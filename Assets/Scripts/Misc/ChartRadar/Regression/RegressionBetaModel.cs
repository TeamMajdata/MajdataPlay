using System;
using System.Collections.Generic;
using System.Linq;
using SimaiRadar.Analysis;

#nullable enable

namespace SimaiRadar.Regression;

public sealed class RegressionBetaModel
{
    public IReadOnlyList<string> InputFeatures => RadarFeatureNames.ModelInputOrder;

    public double Predict(IReadOnlyList<double> rawValues)
    {
        if (rawValues.Count != RegressionBetaParameters.Center.Length)
            throw new ArgumentException("Regression input must contain all seven raw features.", nameof(rawValues));
        var normalized = new double[rawValues.Count];
        for (var index = 0; index < rawValues.Count; index++)
        {
            var value = rawValues[index];
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException("Regression input must be finite.", nameof(rawValues));
            normalized[index] = (value - RegressionBetaParameters.Center[index]) /
                                RegressionBetaParameters.Scale[index];
        }

        var coefficients = RegressionBetaParameters.Coefficients;
        var coefficientIndex = 0;
        var result = RegressionBetaParameters.Intercept;
        for (var index = 0; index < normalized.Length; index++)
            result += coefficients[coefficientIndex++] * normalized[index];
        for (var left = 0; left < normalized.Length; left++)
            for (var right = left; right < normalized.Length; right++)
                result += coefficients[coefficientIndex++] * normalized[left] * normalized[right];
        if (coefficientIndex != coefficients.Length || double.IsNaN(result) || double.IsInfinity(result))
            throw new InvalidOperationException("Regression prediction is invalid.");
        return result;
    }

    public double Predict(IReadOnlyDictionary<string, double> rawFeatures)
    {
        var values = RadarFeatureNames.ModelInputOrder.Select(name =>
            rawFeatures.TryGetValue(name, out var value)
                ? value
                : throw new ArgumentException($"Missing regression feature '{name}'.", nameof(rawFeatures)))
            .ToArray();
        return Predict(values);
    }
}
