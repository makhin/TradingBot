using System;
using System.Collections.Generic;

namespace ComplexBot.Models;

public sealed record IndicatorSnapshot(IReadOnlyDictionary<string, decimal?> Values)
{
    public static IndicatorSnapshot Empty { get; } = new(new Dictionary<string, decimal?>());

    public decimal? GetValue(string key)
        => Values.TryGetValue(key, out var value) ? value : null;

    public static IndicatorSnapshot FromPairs(params (string Key, decimal? Value)[] pairs)
    {
        var values = new Dictionary<string, decimal?>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            values[key] = value;
        }

        return new IndicatorSnapshot(values);
    }
}
