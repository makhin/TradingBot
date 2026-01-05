using ComplexBot.Configuration.Strategy;
using ComplexBot.Services.Trading;

namespace ComplexBot.Configuration.Trading;

public class MultiPairLiveTradingSettings
{
    public bool Enabled { get; set; } = false;
    public decimal TotalCapital { get; set; } = 10000m;
    public List<TradingPairConfig> TradingPairs { get; set; } = new();
    public AllocationMode AllocationMode { get; set; } = AllocationMode.Equal;
    public bool UsePortfolioRiskManager { get; set; } = true;
}

public class TradingPairConfig
{
    public string Symbol { get; set; } = "";
    public string Interval { get; set; } = "FourHour";
    public string Strategy { get; set; } = "ADX";  // ADX, RSI, MA, Ensemble
    public decimal? WeightPercent { get; set; }    // Optional: manual weight (for Weighted mode)

    // Multi-Timeframe Support
    public TradingPairRole Role { get; set; } = TradingPairRole.Primary;
    public FilterMode? FilterMode { get; set; }        // For Filter role: Confirm, Veto, or Score

    // Optional per-pair strategy overrides
    public StrategyConfigSettings? StrategyOverrides { get; set; }
}
