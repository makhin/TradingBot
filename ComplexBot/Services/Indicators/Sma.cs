using System.Linq;
using Skender.Stock.Indicators;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Simple Moving Average
/// </summary>
public class Sma : SkenderIndicatorBase<decimal, SmaResult>
{
    public Sma(int period)
        : base(
            (series, price) => series.AddPrice(price),
            quotes => quotes.GetSma(period).LastOrDefault(),
            result => Value = IndicatorValueConverter.ToDecimal(result?.Sma))
    {
    }
}
