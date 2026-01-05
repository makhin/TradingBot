namespace ComplexBot.Services.RiskManagement;

public record PortfolioRiskSettings
{
    public decimal MaxTotalDrawdownPercent { get; init; } = 25m;  // 25% max portfolio drawdown
    public decimal MaxCorrelatedRiskPercent { get; init; } = 10m;  // 10% max risk on correlated assets
    public int MaxConcurrentPositions { get; init; } = 5;  // Max 5 positions at once
}
