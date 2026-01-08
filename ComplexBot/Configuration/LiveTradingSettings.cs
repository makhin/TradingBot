using ComplexBot.Models;
using ComplexBot.Services.Trading;
using ComplexBot.Services.Lifecycle;

namespace ComplexBot.Configuration;

public class LiveTradingSettings
{
    public string Symbol { get; set; } = "BTCUSDT";
    public KlineInterval Interval { get; set; } = KlineInterval.FourHour;
    public decimal InitialCapital { get; set; } = 10000m;
    public StrategyKind Strategy { get; set; } = StrategyKind.AdxTrendFollowing;
    public bool UseTestnet { get; set; } = true;
    public bool PaperTrade { get; set; } = true;
    public int WarmupCandles { get; set; } = 100;
    public TradingMode TradingMode { get; set; } = TradingMode.Spot;
    public decimal FeeRate { get; set; } = 0.001m;
    public decimal SlippageBps { get; set; } = 2m;
    public decimal MinimumOrderUsd { get; set; } = 10m;
    public int QuantityPrecision { get; set; } = 5;
    public decimal LimitOrderOffsetBps { get; set; } = 5m;
    public int LimitOrderTimeoutSeconds { get; set; } = 5;
    public int StatusLogIntervalMinutes { get; set; } = 5;
    public int BalanceLogIntervalHours { get; set; } = 4;

    // State persistence settings
    public bool EnableStatePersistence { get; set; } = true;
    public ShutdownAction DefaultShutdownAction { get; set; } = ShutdownAction.KeepPositionsAndOrders;

    public LiveTraderSettings ToLiveTraderSettings() => new()
    {
        Symbol = Symbol,
        Interval = Interval,
        InitialCapital = InitialCapital,
        UseTestnet = UseTestnet,
        PaperTrade = PaperTrade,
        WarmupCandles = WarmupCandles,
        TradingMode = TradingMode,
        FeeRate = FeeRate,
        SlippageBps = SlippageBps,
        MinimumOrderUsd = MinimumOrderUsd,
        QuantityPrecision = QuantityPrecision,
        LimitOrderOffsetBps = LimitOrderOffsetBps,
        LimitOrderTimeoutSeconds = LimitOrderTimeoutSeconds,
        StatusLogIntervalMinutes = StatusLogIntervalMinutes,
        BalanceLogIntervalHours = BalanceLogIntervalHours
    };
}
