using TradingBot.Core.RiskManagement;

namespace ComplexBot.Configuration;

public class RiskManagementSettings
{
    public decimal RiskPerTradePercent { get; set; } = 1.5m;
    public decimal MaxPortfolioHeatPercent { get; set; } = 15m;
    public decimal MaxDrawdownPercent { get; set; } = 20m;
    public decimal MaxDailyDrawdownPercent { get; set; } = 3m;
    public decimal AtrStopMultiplier { get; set; } = 2.5m;
    public decimal TakeProfitMultiplier { get; set; } = 1.5m;
    public decimal MinimumEquityUsd { get; set; } = 100m;
    public List<DrawdownRiskPolicy> DrawdownRiskPolicy { get; set; } = new();

    public RiskSettings ToRiskSettings() => new()
    {
        RiskPerTradePercent = RiskPerTradePercent,
        MaxPortfolioHeatPercent = MaxPortfolioHeatPercent,
        MaxDrawdownPercent = MaxDrawdownPercent,
        MaxDailyDrawdownPercent = MaxDailyDrawdownPercent,
        AtrStopMultiplier = AtrStopMultiplier,
        TakeProfitMultiplier = TakeProfitMultiplier,
        MinimumEquityUsd = MinimumEquityUsd,
        DrawdownRiskPolicy = DrawdownRiskPolicy.Count > 0
            ? DrawdownRiskPolicy
            : new RiskSettings().DrawdownRiskPolicy
    };
}
