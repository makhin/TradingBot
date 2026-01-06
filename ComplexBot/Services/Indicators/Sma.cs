using System.Linq;
using Skender.Stock.Indicators;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Simple Moving Average
/// </summary>
public class Sma : IIndicator<decimal>
{
    private readonly int _period;
    private readonly QuoteSeries _series = new();

    public Sma(int period)
    {
        _period = period;
    }

    public decimal? Value { get; private set; }
    public bool IsReady => Value.HasValue;

    public decimal? Update(decimal price)
    {
        _series.AddPrice(price);

        var result = _series.Quotes.GetSma(_period).LastOrDefault();
        Value = IndicatorValueConverter.ToDecimal(result?.Sma);
        return Value;
    }

    public void Reset()
    {
        _series.Reset();
        Value = null;
    }
}
