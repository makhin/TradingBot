using System.Collections.Generic;
using ComplexBot.Models;
using TradingBot.Core.Models;
using ComplexBot.Services.Strategies;

namespace ComplexBot.Services.Backtesting;

public record EnsembleOptimizationSettings
{
    public decimal AdxWeight { get; init; }
    public decimal MaWeight { get; init; }
    public decimal RsiWeight { get; init; }
    public decimal MinimumAgreement { get; init; }
    public bool UseConfidenceWeighting { get; init; }

    public EnsembleSettings ToEnsembleSettings() => new()
    {
        MinimumAgreement = MinimumAgreement,
        UseConfidenceWeighting = UseConfidenceWeighting,
        StrategyWeights = new Dictionary<StrategyKind, decimal>
        {
            [StrategyKind.AdxTrendFollowing] = AdxWeight,
            [StrategyKind.MaCrossover] = MaWeight,
            [StrategyKind.RsiMeanReversion] = RsiWeight
        }
    };
}
