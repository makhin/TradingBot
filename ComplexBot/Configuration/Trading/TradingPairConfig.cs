using ComplexBot.Configuration.Strategy;
using ComplexBot.Services.Trading;

namespace ComplexBot.Configuration.Trading;

public class TradingPairConfig
{
    public string Symbol { get; set; } = "";
    public string Interval { get; set; } = "FourHour";
    public string Strategy { get; set; } = "ADX";  // ADX, RSI, MA, Ensemble
    public decimal? WeightPercent { get; set; }    // Optional: manual weight (for Weighted mode)

    // Multi-Timeframe Support
    public StrategyRole Role { get; set; } = StrategyRole.Primary;
    public FilterMode? FilterMode { get; set; }        // For Filter role: Confirm, Veto, or Score

    // Optional per-pair strategy overrides
    public StrategyConfigSettings? StrategyOverrides { get; set; }
}
