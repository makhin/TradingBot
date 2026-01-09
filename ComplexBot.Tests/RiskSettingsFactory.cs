using TradingBot.Core.RiskManagement;

namespace ComplexBot.Tests;

public static class RiskSettingsFactory
{
    public static RiskSettings CreateDefault() => new()
    {
        RiskPerTradePercent = 1.5m,
        MaxDrawdownPercent = 20m,
        MaxDailyDrawdownPercent = 3m,
        MaxPortfolioHeatPercent = 6m,
        MinimumEquityUsd = 100m,
        AtrStopMultiplier = 2.0m
    };
}
