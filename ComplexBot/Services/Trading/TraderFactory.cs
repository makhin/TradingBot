using Binance.Net.Enums;
using ComplexBot.Configuration;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Notifications;

namespace ComplexBot.Services.Trading;

/// <summary>
/// Factory for creating symbol traders and strategies.
/// Supports multi-pair and multi-timeframe configurations.
/// </summary>
public static class TraderFactory
{
    /// <summary>
    /// Creates a BinanceLiveTrader instance based on configuration.
    /// </summary>
    public static BinanceLiveTrader CreateBinanceTrader(
        TradingPairConfig config,
        string apiKey,
        string apiSecret,
        RiskSettings riskSettings,
        bool useTestnet,
        bool paperTrade,
        decimal initialCapital,
        PortfolioRiskManager? portfolioRiskManager,
        SharedEquityManager? sharedEquityManager,
        TelegramNotifier? telegram,
        BotConfiguration botConfig)
    {
        // Create strategy based on config
        var strategy = CreateStrategy(config, botConfig);

        // Parse interval
        var interval = ParseKlineInterval(config.Interval);

        var settings = new LiveTraderSettings
        {
            Symbol = config.Symbol,
            Interval = interval,
            InitialCapital = initialCapital,
            UseTestnet = useTestnet,
            PaperTrade = paperTrade,
            WarmupCandles = 100,
            TradingMode = TradingMode.Spot,
            FeeRate = 0.001m,
            SlippageBps = 2m
        };

        return new BinanceLiveTrader(
            apiKey,
            apiSecret,
            strategy,
            riskSettings,
            settings,
            telegram,
            portfolioRiskManager,
            sharedEquityManager
        );
    }

    /// <summary>
    /// Creates a strategy instance based on configuration.
    /// Supports strategy-specific overrides.
    /// </summary>
    public static IStrategy CreateStrategy(TradingPairConfig config, BotConfiguration botConfig)
    {
        // Use overrides if provided, otherwise use global config
        var strategySettings = config.StrategyOverrides?.ToStrategySettings()
            ?? botConfig.Strategy.ToStrategySettings();

        return config.Strategy.ToUpperInvariant() switch
        {
            "ADX" => new AdxTrendStrategy(strategySettings),

            "RSI" => new RsiStrategy(
                config.StrategyOverrides != null
                    ? ConvertToRsiSettings(config.StrategyOverrides)
                    : botConfig.RsiStrategy.ToRsiStrategySettings()
            ),

            "MA" => new MaStrategy(
                config.StrategyOverrides != null
                    ? ConvertToMaSettings(config.StrategyOverrides)
                    : botConfig.MaStrategy.ToMaStrategySettings()
            ),

            "ENSEMBLE" => StrategyEnsemble.CreateDefault(
                botConfig.Ensemble.ToEnsembleSettings()
            ),

            _ => new AdxTrendStrategy(strategySettings)
        };
    }

    /// <summary>
    /// Parses KlineInterval from string configuration.
    /// </summary>
    public static KlineInterval ParseKlineInterval(string interval)
    {
        return interval.ToLowerInvariant() switch
        {
            "1m" or "oneminute" => KlineInterval.OneMinute,
            "3m" or "threeminutes" => KlineInterval.ThreeMinutes,
            "5m" or "fiveminutes" => KlineInterval.FiveMinutes,
            "15m" or "fifteenminutes" => KlineInterval.FifteenMinutes,
            "30m" or "thirtyminutes" => KlineInterval.ThirtyMinutes,
            "1h" or "onehour" => KlineInterval.OneHour,
            "2h" or "twohour" => KlineInterval.TwoHour,
            "4h" or "fourhour" => KlineInterval.FourHour,
            "6h" or "sixhour" => KlineInterval.SixHour,
            "8h" or "eighthour" => KlineInterval.EightHour,
            "12h" or "twelvehour" => KlineInterval.TwelveHour,
            "1d" or "oneday" => KlineInterval.OneDay,
            "3d" or "threeday" => KlineInterval.ThreeDay,
            "1w" or "oneweek" => KlineInterval.OneWeek,
            "1mo" or "onemonth" => KlineInterval.OneMonth,
            _ => KlineInterval.FourHour // Default
        };
    }

    /// <summary>
    /// Converts generic StrategyConfigSettings to RSI-specific settings.
    /// </summary>
    private static RsiStrategySettings ConvertToRsiSettings(StrategyConfigSettings config)
    {
        return new RsiStrategySettings
        {
            RsiPeriod = 14,
            OversoldLevel = 30m,
            OverboughtLevel = 70m,
            NeutralZoneLow = 45m,
            NeutralZoneHigh = 55m,
            ExitOnNeutral = false,
            AtrPeriod = config.AtrPeriod,
            AtrStopMultiplier = config.AtrStopMultiplier,
            TakeProfitMultiplier = config.TakeProfitMultiplier,
            TrendFilterPeriod = 50,
            UseTrendFilter = true,
            VolumePeriod = config.VolumePeriod,
            VolumeThreshold = config.VolumeThreshold,
            RequireVolumeConfirmation = config.RequireVolumeConfirmation
        };
    }

    /// <summary>
    /// Converts generic StrategyConfigSettings to MA-specific settings.
    /// </summary>
    private static MaStrategySettings ConvertToMaSettings(StrategyConfigSettings config)
    {
        return new MaStrategySettings
        {
            FastMaPeriod = config.FastEmaPeriod,
            SlowMaPeriod = config.SlowEmaPeriod,
            AtrPeriod = config.AtrPeriod,
            AtrStopMultiplier = config.AtrStopMultiplier,
            TakeProfitMultiplier = config.TakeProfitMultiplier,
            VolumePeriod = config.VolumePeriod,
            VolumeThreshold = config.VolumeThreshold,
            RequireVolumeConfirmation = config.RequireVolumeConfirmation
        };
    }
}
