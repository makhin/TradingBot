using System.Collections.Generic;

namespace TradingBot.Core.Models;

/// <summary>
/// Snapshot of indicator values at a specific point in time
/// Uses string keys for flexibility across different indicator sets
/// </summary>
public sealed record IndicatorSnapshot(IReadOnlyDictionary<string, decimal?> Values)
{
    public static IndicatorSnapshot Empty { get; } = new(new Dictionary<string, decimal?>());

    public decimal? GetValue(string key)
        => Values.GetValueOrDefault(key);

    public static IndicatorSnapshot FromPairs(params (string Key, decimal? Value)[] pairs)
    {
        var values = new Dictionary<string, decimal?>();
        foreach (var (key, value) in pairs)
        {
            values[key] = value;
        }

        return new IndicatorSnapshot(values);
    }
}
