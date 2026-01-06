using System.Linq;
using ComplexBot.Models;
using Skender.Stock.Indicators;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Average True Range - measures volatility
/// </summary>
public class Atr : IIndicator<Candle>
{
    private readonly int _period;
    private readonly QuoteSeries _series = new();

    public Atr(int period = 14)
    {
        _period = period;
    }

    public decimal? Value { get; private set; }
    public bool IsReady => Value.HasValue;

    public decimal? Update(Candle candle)
    {
        _series.AddCandle(candle);

        var result = _series.Quotes.GetAtr(_period).LastOrDefault();
        Value = IndicatorValueConverter.ToDecimal(result?.Atr);
        return Value;
    }

    public void Reset()
    {
        _series.Reset();
        Value = null;
    }
}
