using Binance.Net.Enums;
using ComplexBot.Services.Trading;

namespace ComplexBot.Configuration.Trading;

public class LiveTradingSettings
{
    public string Symbol { get; set; } = "BTCUSDT";
    public string Interval { get; set; } = "FourHour";
    public decimal InitialCapital { get; set; } = 10000m;
    public bool UseTestnet { get; set; } = true;
    public bool PaperTrade { get; set; } = true;
    public int WarmupCandles { get; set; } = 100;
    public string TradingMode { get; set; } = "Spot";
    public decimal FeeRate { get; set; } = 0.001m;
    public decimal SlippageBps { get; set; } = 2m;

    public LiveTraderSettings ToLiveTraderSettings()
    {
        var tradingModeEnum = this.TradingMode == "Spot" 
            ? global::ComplexBot.Services.Trading.TradingMode.Spot 
            : global::ComplexBot.Services.Trading.TradingMode.Futures;
        var interval = ParseInterval(this.Interval);
        
        return new()
        {
            Symbol = this.Symbol,
            Interval = interval,
            InitialCapital = this.InitialCapital,
            UseTestnet = this.UseTestnet,
            PaperTrade = this.PaperTrade,
            WarmupCandles = this.WarmupCandles,
            TradingMode = tradingModeEnum,
            FeeRate = this.FeeRate,
            SlippageBps = this.SlippageBps
        };
    }

    private static KlineInterval ParseInterval(string interval) => interval switch
    {
        "1h" or "OneHour" => KlineInterval.OneHour,
        "4h" or "FourHour" => KlineInterval.FourHour,
        "1d" or "OneDay" => KlineInterval.OneDay,
        _ => KlineInterval.FourHour
    };
}

