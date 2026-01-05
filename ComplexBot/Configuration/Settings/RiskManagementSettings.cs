using ComplexBot.Services.RiskManagement;

namespace ComplexBot.Configuration.Settings;

public class RiskManagementSettings
{
    public decimal RiskPerTradePercent { get; set; } = 1.5m;
    public decimal MaxPortfolioHeatPercent { get; set; } = 15m;
    public decimal MaxDrawdownPercent { get; set; } = 20m;
    public decimal MaxDailyDrawdownPercent { get; set; } = 3m;
    public decimal AtrStopMultiplier { get; set; } = 2.5m;
    public decimal TakeProfitMultiplier { get; set; } = 1.5m;
    public decimal MinimumEquityUsd { get; set; } = 100m;

    public RiskSettings ToRiskSettings() => new()
    {
        RiskPerTradePercent = RiskPerTradePercent,
        MaxPortfolioHeatPercent = MaxPortfolioHeatPercent,
        MaxDrawdownPercent = MaxDrawdownPercent,
        MaxDailyDrawdownPercent = MaxDailyDrawdownPercent,
        AtrStopMultiplier = AtrStopMultiplier,
        TakeProfitMultiplier = TakeProfitMultiplier,
        MinimumEquityUsd = MinimumEquityUsd
    };
}
