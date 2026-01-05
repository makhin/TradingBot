using System.Linq;
using ComplexBot.Services.Backtesting;
using ComplexBot.Services.Trading;

namespace ComplexBot.Configuration.Optimization;

public class MultiTimeframeOptimizerSettings
{
    public bool OptimizeFilters { get; set; } = true;
    public string[] FilterIntervalCandidates { get; set; } = ["FifteenMinutes", "ThirtyMinutes", "OneHour"];
    public decimal[] RsiOverboughtRange { get; set; } = [65m, 70m, 75m, 80m];
    public decimal[] RsiOversoldRange { get; set; } = [20m, 25m, 30m, 35m];
    public decimal[] AdxMinThresholdRange { get; set; } = [15m, 20m, 25m, 30m];
    public string[] FilterModesToTest { get; set; } = ["Confirm", "Veto"];
    public bool TestNoFilterBaseline { get; set; } = true;
    public string OptimizeFor { get; set; } = "RiskAdjusted";

    public MultiTimeframeOptimizationSettings ToSettings() => new()
    {
        OptimizeFor = ParseOptimizationTarget(OptimizeFor),
        OptimizeFilters = OptimizeFilters,
        FilterIntervalCandidates = FilterIntervalCandidates,
        RsiOverboughtRange = RsiOverboughtRange,
        RsiOversoldRange = RsiOversoldRange,
        AdxMinThresholdRange = AdxMinThresholdRange,
        FilterModesToTest = FilterModesToTest.Select(ParseFilterMode).ToArray(),
        TestNoFilterBaseline = TestNoFilterBaseline
    };

    private static OptimizationTarget ParseOptimizationTarget(string target) => target switch
    {
        "RiskAdjusted" => OptimizationTarget.RiskAdjusted,
        "SharpeRatio" => OptimizationTarget.SharpeRatio,
        "SortinoRatio" => OptimizationTarget.SortinoRatio,
        "ProfitFactor" => OptimizationTarget.ProfitFactor,
        "TotalReturn" => OptimizationTarget.TotalReturn,
        _ => OptimizationTarget.RiskAdjusted
    };

    private static FilterMode ParseFilterMode(string mode) => mode switch
    {
        "Confirm" => FilterMode.Confirm,
        "Veto" => FilterMode.Veto,
        "Score" => FilterMode.Score,
        _ => FilterMode.Confirm
    };
}
