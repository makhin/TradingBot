namespace ComplexBot.Services.Backtesting;

public record OptimizerSettings
{
    // Data split
    public decimal InSampleRatio { get; init; } = 0.7m;

    // Optimization target
    public OptimizationTarget OptimizeFor { get; init; } = OptimizationTarget.RiskAdjusted;

    // Minimum requirements
    public int MinTrades { get; init; } = 30;
    public decimal MinRobustnessRatio { get; init; } = 0.5m; // OOS должен быть >= 50% от IS
    public int TopResultsCount { get; init; } = 10;

    // Parameter ranges for grid search
    public int[] AdxPeriodRange { get; init; } = [10, 14, 20];
    public decimal[] AdxThresholdRange { get; init; } = [20m, 25m, 30m];
    public int[] FastEmaRange { get; init; } = [10, 15, 20, 25];
    public int[] SlowEmaRange { get; init; } = [40, 50, 60, 80];
    public decimal[] AtrMultiplierRange { get; init; } = [2.0m, 2.5m, 3.0m];
    public decimal[] VolumeThresholdRange { get; init; } = [1.0m, 1.5m, 2.0m];
}
