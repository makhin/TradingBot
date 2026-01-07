using System.Linq;
using ComplexBot.Models;
using Skender.Stock.Indicators;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Average True Range - measures volatility
/// </summary>
public class Atr : SkenderIndicatorBase<Candle, AtrResult>
{
    public Atr(int period = 14)
        : base(
            (series, candle) => series.AddCandle(candle),
            quotes => quotes.GetAtr(period).LastOrDefault(),
            _ => { })
    {
    }

    protected override void OnUpdate(AtrResult? result)
    {
        Value = IndicatorValueConverter.ToDecimal(result?.Atr);
    }
}
