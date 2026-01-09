using System.Collections.Generic;
using TradingBot.Core.Models;
using ComplexBot.Models;

namespace ComplexBot.Services.Strategies;

public record EnsembleSettings
{
    /// <summary>
    /// Minimum weighted agreement required for signal (0.0-1.0)
    /// Default: 0.6 (60% agreement)
    /// </summary>
    public decimal MinimumAgreement { get; init; } = 0.6m;

    /// <summary>
    /// Whether to weight votes by strategy confidence
    /// If true: score = sum(weight * confidence) / totalWeight
    /// If false: score = sum(weight) / totalWeight
    /// </summary>
    public bool UseConfidenceWeighting { get; init; } = true;

    /// <summary>
    /// Strategy weights by name. Used by CreateDefault() to set initial weights.
    /// Weights must sum to <= 1.0 but don't need to sum exactly to 1.0.
    ///
    /// Default weights balance TWO philosophies:
    /// - Trend Following (ADX 50% + MA 25% = 75%) - dominant in strong trends
    /// - Mean Reversion (RSI 25%) - catches pullbacks, filtered by trend strategies
    /// </summary>
    public Dictionary<StrategyKind, decimal> StrategyWeights { get; init; } = new()
    {
        [StrategyKind.AdxTrendFollowing] = 0.5m,  // Primary trend follower
        [StrategyKind.MaCrossover] = 0.25m,       // Secondary trend follower
        [StrategyKind.RsiMeanReversion] = 0.25m   // Counter-trend (mean reversion)
    };
}
