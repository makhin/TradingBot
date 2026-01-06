using ComplexBot.Models;
using ComplexBot.Services.Trading;

namespace ComplexBot.Configuration;

public class LiveTradingSettings
{
    public string Symbol { get; set; } = "BTCUSDT";
    public KlineInterval Interval { get; set; } = KlineInterval.FourHour;
    public decimal InitialCapital { get; set; } = 10000m;
    public bool UseTestnet { get; set; } = true;
    public bool PaperTrade { get; set; } = true;
    public int WarmupCandles { get; set; } = 100;
    public TradingMode TradingMode { get; set; } = TradingMode.Spot;
    public decimal FeeRate { get; set; } = 0.001m;
    public decimal SlippageBps { get; set; } = 2m;

    public LiveTraderSettings ToLiveTraderSettings() => new()
    {
        Symbol = Symbol,
        Interval = Interval,
        InitialCapital = InitialCapital,
        UseTestnet = UseTestnet,
        PaperTrade = PaperTrade,
        WarmupCandles = WarmupCandles,
        TradingMode = TradingMode,
        FeeRate = FeeRate,
        SlippageBps = SlippageBps
    };
}
