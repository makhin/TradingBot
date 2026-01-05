namespace ComplexBot.Services.Backtesting;

public record WalkForwardSettings
{
    public decimal InSampleRatio { get; init; } = 0.7m;      // 70% for optimization
    public decimal OutOfSampleRatio { get; init; } = 0.2m;  // 20% for validation
    public decimal StepRatio { get; init; } = 0.1m;         // 10% step forward
    public decimal MinWfeThreshold { get; init; } = 50m;    // Minimum 50% WFE
    public decimal MinConsistencyThreshold { get; init; } = 60m;  // 60% profitable periods
    public decimal MinSharpeThreshold { get; init; } = 0.5m;  // Minimum Sharpe
}
