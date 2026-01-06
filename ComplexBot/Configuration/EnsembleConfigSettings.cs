using System.Collections.Generic;
using ComplexBot.Models;
using ComplexBot.Services.Strategies;

namespace ComplexBot.Configuration;

public class EnsembleConfigSettings
{
    public bool Enabled { get; set; } = false;
    public decimal MinimumAgreement { get; set; } = 0.6m;
    public bool UseConfidenceWeighting { get; set; } = true;
    public Dictionary<StrategyKind, decimal> StrategyWeights { get; set; } = new()
    {
        [StrategyKind.AdxTrendFollowing] = 0.5m,
        [StrategyKind.MaCrossover] = 0.25m,
        [StrategyKind.RsiMeanReversion] = 0.25m
    };

    public EnsembleSettings ToEnsembleSettings() => new()
    {
        MinimumAgreement = MinimumAgreement,
        UseConfidenceWeighting = UseConfidenceWeighting,
        StrategyWeights = StrategyWeights ?? new Dictionary<StrategyKind, decimal>()
    };
}
