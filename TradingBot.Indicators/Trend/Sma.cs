using System.Linq;
using TradingBot.Indicators.Base;
using TradingBot.Indicators.Utils;
using Skender.Stock.Indicators;

namespace TradingBot.Indicators.Trend;

/// <summary>
/// Simple Moving Average
/// </summary>
public class Sma : SkenderIndicatorBase<decimal, SmaResult>
{
    public Sma(int period)
        : base(
            (series, price) => series.AddPrice(price),
            quotes => quotes.GetSma(period).LastOrDefault(),
            _ => { })
    {
    }

    protected override void OnUpdate(SmaResult? result)
    {
        Value = IndicatorValueConverter.ToDecimal(result?.Sma);
    }
}
