using System.Linq;
using Skender.Stock.Indicators;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Relative Strength Index
/// </summary>
public class Rsi : SkenderIndicatorBase<decimal, RsiResult>
{
    public Rsi(int period = 14)
        : base(
            (series, price) => series.AddPrice(price),
            quotes => quotes.GetRsi(period).LastOrDefault(),
            _ => { })
    {
    }

    protected override void OnUpdate(RsiResult? result)
    {
        Value = IndicatorValueConverter.ToDecimal(result?.Rsi);
    }
}
