using ComplexBot.Services.Backtesting;

namespace ComplexBot.Configuration;

public class OptimizationSettings
{
    public decimal InSampleRatio { get; set; } = 0.7m;
    public OptimizationTarget OptimizeFor { get; set; } = OptimizationTarget.RiskAdjusted;
    public PerformanceFitnessPolicy Policy { get; set; } = new();
    public decimal MinRobustnessRatio { get; set; } = 0.5m;
    public int TopResultsCount { get; set; } = 10;
    public int[] AdxPeriodRange { get; set; } = [10, 14, 20];
    public decimal[] AdxThresholdRange { get; set; } = [20m, 25m, 30m];
    public int[] FastEmaRange { get; set; } = [10, 15, 20, 25];
    public int[] SlowEmaRange { get; set; } = [40, 50, 60, 80];
    public decimal[] AtrMultiplierRange { get; set; } = [2.0m, 2.5m, 3.0m];
    public decimal[] VolumeThresholdRange { get; set; } = [1.0m, 1.5m, 2.0m];

    public OptimizerSettings ToOptimizerSettings() => new()
    {
        InSampleRatio = InSampleRatio,
        OptimizeFor = OptimizeFor,
        Policy = Policy,
        MinRobustnessRatio = MinRobustnessRatio,
        TopResultsCount = TopResultsCount,
        AdxPeriodRange = AdxPeriodRange,
        AdxThresholdRange = AdxThresholdRange,
        FastEmaRange = FastEmaRange,
        SlowEmaRange = SlowEmaRange,
        AtrMultiplierRange = AtrMultiplierRange,
        VolumeThresholdRange = VolumeThresholdRange
    };
}
