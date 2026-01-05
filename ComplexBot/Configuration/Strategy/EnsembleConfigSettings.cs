using ComplexBot.Services.Strategies;

namespace ComplexBot.Configuration.Strategy;

public class EnsembleConfigSettings
{
    public bool Enabled { get; set; } = false;
    public decimal MinimumAgreement { get; set; } = 0.6m;
    public bool UseConfidenceWeighting { get; set; } = true;
    public Dictionary<string, decimal> StrategyWeights { get; set; } = new()
    {
        ["ADX Trend Following + Volume"] = 0.5m,
        ["MA Crossover"] = 0.25m,
        ["RSI Mean Reversion"] = 0.25m
    };

    public EnsembleSettings ToEnsembleSettings() => new()
    {
        MinimumAgreement = MinimumAgreement,
        UseConfidenceWeighting = UseConfidenceWeighting,
        StrategyWeights = StrategyWeights
    };
}
