using System.Linq;
using Skender.Stock.Indicators;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Exponential Moving Average
/// </summary>
public class Ema : SkenderIndicatorBase<decimal, EmaResult>
{
    public Ema(int period)
        : base(
            (series, price) => series.AddPrice(price),
            quotes => quotes.GetEma(period).LastOrDefault(),
            result => Value = IndicatorValueConverter.ToDecimal(result?.Ema))
    {
    }
}
