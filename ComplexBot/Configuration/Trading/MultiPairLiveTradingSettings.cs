using ComplexBot.Configuration.Strategy;

namespace ComplexBot.Configuration.Trading;

public class MultiPairLiveTradingSettings
{
    public bool Enabled { get; set; } = false;
    public decimal TotalCapital { get; set; } = 10000m;
    public List<TradingPairConfig> TradingPairs { get; set; } = new();
    public string AllocationMode { get; set; } = "Equal";
    public bool UsePortfolioRiskManager { get; set; } = true;
}

public class TradingPairConfig
{
    public string Symbol { get; set; } = "";
    public string Interval { get; set; } = "FourHour";
    public string Strategy { get; set; } = "ADX";  // ADX, RSI, MA, Ensemble
    public decimal? WeightPercent { get; set; }    // Optional: manual weight (for Weighted mode)

    // Multi-Timeframe Support
    public string Role { get; set; } = "Primary";  // Primary, Filter, Exit
    public string? FilterMode { get; set; }        // For Filter role: "Confirm", "Veto", "Score"

    // Optional per-pair strategy overrides
    public StrategyConfigSettings? StrategyOverrides { get; set; }
}
