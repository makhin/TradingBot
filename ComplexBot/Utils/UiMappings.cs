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

    private static readonly IReadOnlyDictionary<StrategyKind, string> StrategyLabels =
        new Dictionary<StrategyKind, string>
        {
            { StrategyKind.AdxTrendFollowing, "ADX Trend Following (Recommended)" },
            { StrategyKind.RsiMeanReversion, "RSI Mean Reversion" },
            { StrategyKind.MaCrossover, "MA Crossover" },
            { StrategyKind.StrategyEnsemble, "Strategy Ensemble (All Combined)" }
        };

    private static readonly IReadOnlyList<StrategyKind> StrategyOrder =
    [
        StrategyKind.AdxTrendFollowing,
        StrategyKind.RsiMeanReversion,
        StrategyKind.MaCrossover,
        StrategyKind.StrategyEnsemble
    ];

    private static readonly IReadOnlyDictionary<StrategyKind, string> StrategyShortNames =
        new Dictionary<StrategyKind, string>
        {
            { StrategyKind.AdxTrendFollowing, "ADX" },
            { StrategyKind.RsiMeanReversion, "RSI" },
            { StrategyKind.MaCrossover, "MA" },
            { StrategyKind.StrategyEnsemble, "Ensemble" }
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

    public static IReadOnlyList<AppMode> AppModes => AppMenuOrder;
    public static IReadOnlyList<StrategyKind> StrategyModes => StrategyOrder;
    public static IReadOnlyList<KlineInterval> IntervalModes => IntervalMenuOrder;
    public static IReadOnlyList<TradingMode> TradingModes => TradingModeOrder;

    public static string GetAppModeLabel(AppMode mode) => AppModeLabels[mode];
    public static string GetStrategyLabel(StrategyKind kind) => StrategyLabels[kind];
    public static string GetStrategyShortName(StrategyKind kind) => StrategyShortNames[kind];
    public static string GetIntervalLabel(KlineInterval interval) => IntervalLabels[interval];
    public static string GetTradingModeLabel(TradingMode mode) => TradingModeLabels[mode];

    public static bool TryGetAppModeFromEnv(string value, out AppMode mode)
        => AppModeEnvMap.TryGetValue(value, out mode);

    public static IEnumerable<string> AppModeEnvValues => AppModeEnvMap.Keys.OrderBy(key => key);
}
