namespace ComplexBot.Services.Backtesting;

public record FullEnsembleOptimizerConfig
{
    public decimal WeightMin { get; init; } = 0.1m;
    public decimal WeightMax { get; init; } = 0.8m;
}
