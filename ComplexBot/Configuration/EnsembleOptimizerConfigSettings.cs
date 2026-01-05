using ComplexBot.Services.Backtesting;

namespace ComplexBot.Configuration;

public class EnsembleOptimizerConfigSettings
{
    public decimal WeightMin { get; set; } = 0.05m;
    public decimal WeightMax { get; set; } = 1.0m;
    public decimal MinimumAgreementMin { get; set; } = 0.4m;
    public decimal MinimumAgreementMax { get; set; } = 0.8m;
    public bool AllowConfidenceWeightingToggle { get; set; } = true;
    public bool DefaultUseConfidenceWeighting { get; set; } = true;
    public int MinTrades { get; set; } = 20;

    public EnsembleOptimizerConfig ToEnsembleOptimizerConfig() => new()
    {
        WeightMin = WeightMin,
        WeightMax = WeightMax,
        MinimumAgreementMin = MinimumAgreementMin,
        MinimumAgreementMax = MinimumAgreementMax,
        AllowConfidenceWeightingToggle = AllowConfidenceWeightingToggle,
        DefaultUseConfidenceWeighting = DefaultUseConfidenceWeighting,
        MinTrades = MinTrades
    };
}
