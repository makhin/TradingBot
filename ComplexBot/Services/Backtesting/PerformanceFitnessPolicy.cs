namespace ComplexBot.Services.Backtesting;

public record PerformanceFitnessPolicy
{
    public int MinTrades { get; init; } = 30;
    public decimal MaxDrawdownPercent { get; init; } = 30m;
    public decimal InsufficientTradesPenalty { get; init; } = -100m;
    public decimal MaxDrawdownPenalty { get; init; } = -999m;
    public decimal InvalidSettingsPenalty { get; init; } = -1000m;
    public decimal DrawdownPenaltyThresholdPercent { get; init; } = 20m;
    public decimal DrawdownPenaltyFactor { get; init; } = 0.1m;
}
