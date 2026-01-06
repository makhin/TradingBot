using System;
using System.Collections.Generic;

namespace ComplexBot.Models;

public sealed record IndicatorSnapshot(IReadOnlyDictionary<IndicatorValueKey, decimal?> Values)
{
    public static IndicatorSnapshot Empty { get; } = new(new Dictionary<IndicatorValueKey, decimal?>());

    public decimal? GetValue(IndicatorValueKey key)
        => Values.TryGetValue(key, out var value) ? value : null;

    public static IndicatorSnapshot FromPairs(params (IndicatorValueKey Key, decimal? Value)[] pairs)
    {
        var values = new Dictionary<IndicatorValueKey, decimal?>();
        foreach (var (key, value) in pairs)
        {
            values[key] = value;
        }

        return new IndicatorSnapshot(values);
    }
}
