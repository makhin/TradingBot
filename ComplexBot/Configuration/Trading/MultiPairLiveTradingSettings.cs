namespace ComplexBot.Configuration.Trading;

public class MultiPairLiveTradingSettings
{
    public bool Enabled { get; set; } = false;
    public decimal TotalCapital { get; set; } = 10000m;
    public List<TradingPairConfig> TradingPairs { get; set; } = new();
    public CapitalAllocationMode AllocationMode { get; set; } = CapitalAllocationMode.Equal;
    public bool UsePortfolioRiskManager { get; set; } = true;
}
