using TradingBot.Core.Models;
using ComplexBot.Models;

namespace ComplexBot.Services.Trading;

public record LiveTraderSettings
{
    public string Symbol { get; init; } = "BTCUSDT";
    public KlineInterval Interval { get; init; } = KlineInterval.FourHour;
    public decimal InitialCapital { get; init; } = 10000m;
    public bool UseTestnet { get; init; } = true;
    public bool PaperTrade { get; init; } = true;  // Paper trade by default for safety
    public int WarmupCandles { get; init; } = 100;
    public TradingMode TradingMode { get; init; } = TradingMode.Spot;
    public decimal FeeRate { get; init; } = 0.001m;
    public decimal SlippageBps { get; init; } = 2m;
    public decimal MinimumOrderUsd { get; init; } = 10m;
    public int QuantityPrecision { get; init; } = 5;
    public decimal LimitOrderOffsetBps { get; init; } = 5m;
    public int LimitOrderTimeoutSeconds { get; init; } = 5;
    public int StatusLogIntervalMinutes { get; init; } = 5;
    public int BalanceLogIntervalHours { get; init; } = 4;
}
