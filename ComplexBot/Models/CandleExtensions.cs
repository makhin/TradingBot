using Skender.Stock.Indicators;

namespace ComplexBot.Models;

public static class CandleExtensions
{
    public static Quote ToQuote(this Candle candle)
        => new()
        {
            Date = candle.CloseTime,
            Open = candle.Open,
            High = candle.High,
            Low = candle.Low,
            Close = candle.Close,
            Volume = candle.Volume
        };
}
