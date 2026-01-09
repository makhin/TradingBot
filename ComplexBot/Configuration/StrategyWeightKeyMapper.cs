using System;
using System.Collections.Generic;
using TradingBot.Core.Models;
using ComplexBot.Models;

namespace ComplexBot.Configuration;

public static class StrategyWeightKeyMapper
{
    private static readonly IReadOnlyDictionary<string, StrategyKind> LegacyKeys =
        new Dictionary<string, StrategyKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["ADX Trend Following + Volume"] = StrategyKind.AdxTrendFollowing,
            ["MA Crossover"] = StrategyKind.MaCrossover,
            ["RSI Mean Reversion"] = StrategyKind.RsiMeanReversion
        };

    public static bool TryGetStrategyKind(string key, out StrategyKind kind)
    {
        if (Enum.TryParse(key, ignoreCase: true, out kind))
        {
            return true;
        }

        return LegacyKeys.TryGetValue(key, out kind);
    }
}
