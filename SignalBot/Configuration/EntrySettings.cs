namespace SignalBot.Configuration;

/// <summary>
/// Entry order settings
/// </summary>
public class EntrySettings
{
    public decimal MaxPriceDeviationPercent { get; set; } = 0.5m;
    public string DeviationAction { get; set; } = "Skip"; // Skip, Adjust, Wait
    public bool UseLimitOrder { get; set; } = false;
    public TimeSpan LimitOrderTtl { get; set; } = TimeSpan.FromMinutes(5);
    public decimal MaxSlippagePercent { get; set; } = 0.3m;
}
