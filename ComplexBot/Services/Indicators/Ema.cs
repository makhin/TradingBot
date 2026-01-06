using System.Linq;
using Skender.Stock.Indicators;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Exponential Moving Average
/// </summary>
public class Ema : IIndicator<decimal>
{
    private readonly int _period;
    private readonly QuoteSeries _series = new();

    public Ema(int period)
    {
        _period = period;
    }

    public decimal? Value { get; private set; }
    public bool IsReady => Value.HasValue;

    public decimal? Update(decimal price)
    {
        _series.AddPrice(price);

        var result = _series.Quotes.GetEma(_period).LastOrDefault();
        Value = IndicatorValueConverter.ToDecimal(result?.Ema);
        return Value;
    }

    public void Reset()
    {
        _series.Reset();
        Value = null;
    }
}
