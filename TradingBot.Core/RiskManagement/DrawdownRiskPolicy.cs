namespace TradingBot.Core.RiskManagement;

public record DrawdownRiskPolicy
{
    public decimal DrawdownThresholdPercent { get; init; }
    public decimal RiskMultiplier { get; init; }
}
