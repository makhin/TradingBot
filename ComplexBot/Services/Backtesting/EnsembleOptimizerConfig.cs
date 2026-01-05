namespace ComplexBot.Services.Backtesting;

public record EnsembleOptimizerConfig
{
    public decimal WeightMin { get; init; } = 0.05m;
    public decimal WeightMax { get; init; } = 1.0m;
    public decimal MinimumAgreementMin { get; init; } = 0.4m;
    public decimal MinimumAgreementMax { get; init; } = 0.8m;
    public bool AllowConfidenceWeightingToggle { get; init; } = true;
    public bool DefaultUseConfidenceWeighting { get; init; } = true;
    public int MinTrades { get; init; } = 20;
}
