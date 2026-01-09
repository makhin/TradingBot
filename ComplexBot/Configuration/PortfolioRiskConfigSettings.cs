using System.Collections.Generic;
using TradingBot.Core.RiskManagement;

namespace ComplexBot.Configuration;

public class PortfolioRiskConfigSettings
{
    public decimal MaxTotalDrawdownPercent { get; set; } = 25m;
    public decimal MaxCorrelatedRiskPercent { get; set; } = 10m;
    public int MaxConcurrentPositions { get; set; } = 5;
    public Dictionary<string, string[]> CorrelationGroups { get; set; } = new();

    public PortfolioRiskSettings ToPortfolioRiskSettings() => new()
    {
        MaxTotalDrawdownPercent = MaxTotalDrawdownPercent,
        MaxCorrelatedRiskPercent = MaxCorrelatedRiskPercent,
        MaxConcurrentPositions = MaxConcurrentPositions
    };
}
