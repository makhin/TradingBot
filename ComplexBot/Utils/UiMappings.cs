using TradingBot.Core.Models;
using ComplexBot.Services.Strategies;
using ComplexBot.Models;

namespace ComplexBot.Utils;

public static class UiMappings
{
    private static readonly IReadOnlyDictionary<AppMode, string> AppModeLabels = new Dictionary<AppMode, string>
    {
        { AppMode.Backtest, "Backtest" },
        { AppMode.ParameterOptimization, "Parameter Optimization" },
        { AppMode.WalkForwardAnalysis, "Walk-Forward Analysis" },
        { AppMode.MonteCarloSimulation, "Monte Carlo Simulation" },
        { AppMode.LiveTradingPaper, "Live Trading (Paper)" },
        { AppMode.LiveTradingReal, "Live Trading (Real)" },
        { AppMode.DownloadData, "Download Data" },
        { AppMode.ConfigurationSettings, "Configuration Settings" },
        { AppMode.ResetToDefaults, "Reset to Defaults" },
        { AppMode.Exit, "Exit" }
    };

    private static readonly IReadOnlyList<AppMode> AppMenuOrder =
    [
        AppMode.Backtest,
        AppMode.ParameterOptimization,
        AppMode.WalkForwardAnalysis,
        AppMode.MonteCarloSimulation,
        AppMode.LiveTradingPaper,
        AppMode.LiveTradingReal,
        AppMode.DownloadData,
        AppMode.ConfigurationSettings,
        AppMode.ResetToDefaults,
        AppMode.Exit
    ];

    private static readonly IReadOnlyDictionary<string, AppMode> AppModeEnvMap =
        new Dictionary<string, AppMode>(StringComparer.OrdinalIgnoreCase)
        {
            { "live", AppMode.LiveTradingPaper },
            { "live-real", AppMode.LiveTradingReal },
            { "backtest", AppMode.Backtest },
            { "optimize", AppMode.ParameterOptimization },
            { "walkforward", AppMode.WalkForwardAnalysis },
            { "montecarlo", AppMode.MonteCarloSimulation },
            { "download", AppMode.DownloadData }
        };


    private static readonly IReadOnlyDictionary<KlineInterval, string> IntervalLabels =
        new Dictionary<KlineInterval, string>
        {
            { KlineInterval.OneMinute, "1m" },
            { KlineInterval.FiveMinutes, "5m" },
            { KlineInterval.FifteenMinutes, "15m" },
            { KlineInterval.ThirtyMinutes, "30m" },
            { KlineInterval.OneHour, "1h" },
            { KlineInterval.FourHour, "4h" },
            { KlineInterval.OneDay, "1d" },
            { KlineInterval.OneWeek, "1w" }
        };

    private static readonly IReadOnlyList<KlineInterval> IntervalMenuOrder =
    [
        KlineInterval.OneHour,
        KlineInterval.FourHour,
        KlineInterval.OneDay
    ];

    private static readonly IReadOnlyDictionary<TradingMode, string> TradingModeLabels =
        new Dictionary<TradingMode, string>
        {
            { TradingMode.Spot, "Spot (no margin)" },
            { TradingMode.Futures, "Futures/Margin" }
        };

    private static readonly IReadOnlyList<TradingMode> TradingModeOrder =
    [
        TradingMode.Spot,
        TradingMode.Futures
    ];

    private static readonly IReadOnlyDictionary<OptimizationMode, string> OptimizationModeLabels =
        new Dictionary<OptimizationMode, string>
        {
            { OptimizationMode.Full, "Full Grid Search" },
            { OptimizationMode.Genetic, "Genetic" },
            { OptimizationMode.Quick, "Quick Test" },
            { OptimizationMode.EnsembleWeightsOnly, "Weights Only" },
            { OptimizationMode.EnsembleFull, "Full - All Parameters" }
        };

    public static IReadOnlyList<AppMode> AppModes => AppMenuOrder;
    public static IReadOnlyList<StrategyKind> StrategyModes => StrategyRegistry.StrategyOrder;
    public static IReadOnlyList<KlineInterval> IntervalModes => IntervalMenuOrder;
    public static IReadOnlyList<TradingMode> TradingModes => TradingModeOrder;

    public static string GetAppModeLabel(AppMode mode) => AppModeLabels[mode];
    public static string GetStrategyLabel(StrategyKind kind) => StrategyRegistry.GetStrategyLabel(kind);
    public static string GetStrategyShortName(StrategyKind kind) => StrategyRegistry.GetStrategyShortName(kind);
    public static string GetIntervalLabel(KlineInterval interval) => IntervalLabels[interval];
    public static string GetTradingModeLabel(TradingMode mode) => TradingModeLabels[mode];
    public static string GetOptimizationScenarioLabel(OptimizationScenario scenario) =>
        $"{GetStrategyLabel(scenario.Kind)} ({OptimizationModeLabels[scenario.Mode]})";

    public static bool TryGetAppModeFromEnv(string value, out AppMode mode)
        => AppModeEnvMap.TryGetValue(value, out mode);

    public static IEnumerable<string> AppModeEnvValues => AppModeEnvMap.Keys.OrderBy(key => key);
}
