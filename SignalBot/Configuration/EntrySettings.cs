using SignalBot.Models;

namespace SignalBot.Configuration;

/// <summary>
/// Entry order settings
/// </summary>
public class EntrySettings
{
    public decimal MaxPriceDeviationPercent { get; set; } = 2.5m;
    public PriceDeviationAction DeviationAction { get; set; } = PriceDeviationAction.Skip;
    public bool UseLimitOrder { get; set; } = false;
    public LimitOrderPricing LimitPricing { get; set; } = LimitOrderPricing.AtEntry;
    public TimeSpan LimitOrderTtl { get; set; } = TimeSpan.FromMinutes(5);
    public decimal MaxSlippagePercent { get; set; } = 0.3m;
}
