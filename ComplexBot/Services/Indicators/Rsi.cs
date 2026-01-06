using System;
using System.Linq;
using Skender.Stock.Indicators;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Relative Strength Index
/// </summary>
public class Rsi : IIndicator<decimal>
{
    private readonly int _period;
    private readonly QuoteSeries _series = new();

    public Rsi(int period = 14)
    {
        _period = period;
    }

    public decimal? Value { get; private set; }
    public bool IsReady => Value.HasValue;

    public decimal? Update(decimal price)
    {
        _series.AddPrice(price);

        var result = _series.Quotes.GetRsi(_period).LastOrDefault();
        Value = IndicatorValueConverter.ToDecimal(result?.Rsi);
        return Value;
    }

    public void Reset()
    {
        _series.Reset();
        Value = null;
    }
}
