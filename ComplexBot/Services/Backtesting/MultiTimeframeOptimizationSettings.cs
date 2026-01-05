using ComplexBot.Services.Trading;

namespace ComplexBot.Services.Backtesting;

public record MultiTimeframeOptimizationSettings
{
    public OptimizationTarget OptimizeFor { get; init; } = OptimizationTarget.RiskAdjusted;
    public bool OptimizeFilters { get; init; } = true;
    public string[] FilterIntervalCandidates { get; init; } = ["FifteenMinutes", "ThirtyMinutes", "OneHour"];
    public decimal[] RsiOverboughtRange { get; init; } = [65m, 70m, 75m, 80m];
    public decimal[] RsiOversoldRange { get; init; } = [20m, 25m, 30m, 35m];
    public decimal[] AdxMinThresholdRange { get; init; } = [15m, 20m, 25m, 30m];
    public decimal[] AdxStrongThresholdRange { get; init; } = [25m, 30m, 35m];
    public FilterMode[] FilterModesToTest { get; init; } = [FilterMode.Confirm, FilterMode.Veto];
    public bool TestNoFilterBaseline { get; init; } = true;
}
