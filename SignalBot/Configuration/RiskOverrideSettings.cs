namespace SignalBot.Configuration;

/// <summary>
/// Risk override settings
/// </summary>
public class RiskOverrideSettings
{
    public bool Enabled { get; set; } = true;
    public int MaxLeverage { get; set; } = 10;
    public bool UseSignalLeverage { get; set; } = false;

    public string StopLossMode { get; set; } = "Calculate"; // FromSignal, Calculate
    public decimal StopLossPercent { get; set; } = 2.0m;
    public decimal SafeDistanceFromLiquidation { get; set; } = 0.3m;

    public decimal RiskPerTradePercent { get; set; } = 1.0m;
    public decimal MaxDrawdownPercent { get; set; } = 20.0m;
    public decimal MaxDailyLossPercent { get; set; } = 5.0m;
}
