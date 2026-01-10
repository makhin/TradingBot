namespace SignalBot.Configuration;

/// <summary>
/// Position sizing settings
/// </summary>
public class PositionSizingSettings
{
    public string DefaultMode { get; set; } = "FixedAmount"; // FixedAmount, RiskPercent, FixedMargin
    public decimal DefaultRiskPercent { get; set; } = 1.0m;
    public decimal DefaultFixedAmount { get; set; } = 100.0m;
    public decimal DefaultFixedMargin { get; set; } = 50.0m;

    public Dictionary<string, SymbolSizingOverride> SymbolOverrides { get; set; } = new();

    public PositionSizingLimits Limits { get; set; } = new();
}

public class SymbolSizingOverride
{
    public decimal? FixedAmount { get; set; }
    public decimal? RiskPercent { get; set; }
}

public class PositionSizingLimits
{
    public decimal MinPositionUsdt { get; set; } = 10.0m;
    public decimal MaxPositionUsdt { get; set; } = 1000.0m;
    public decimal MaxPositionPercent { get; set; } = 25.0m;
    public decimal MaxTotalExposurePercent { get; set; } = 80.0m;
}
