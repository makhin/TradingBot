namespace ComplexBot.Services.RiskManagement;

public record RiskSettings
{
    public decimal RiskPerTradePercent { get; init; } = 1.5m;  // 1.5% per trade
    public decimal MaxPortfolioHeatPercent { get; init; } = 15m;  // 15% max heat
    public decimal MaxDrawdownPercent { get; init; } = 20m;  // 20% circuit breaker
    public decimal MaxDailyDrawdownPercent { get; init; } = 3m;  // 3% daily loss limit
    public decimal AtrStopMultiplier { get; init; } = 2.5m;  // 2.5x ATR for stops
    public decimal TakeProfitMultiplier { get; init; } = 1.5m;  // 1.5:1 reward:risk
    public decimal MinimumEquityUsd { get; init; } = 100m;  // Minimum $100 to trade
}
