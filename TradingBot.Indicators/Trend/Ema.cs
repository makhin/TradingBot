using System.Linq;
using TradingBot.Indicators.Base;
using TradingBot.Indicators.Utils;
using Skender.Stock.Indicators;

namespace TradingBot.Indicators.Trend;

/// <summary>
/// Exponential Moving Average
/// </summary>
public class Ema : SkenderIndicatorBase<decimal, EmaResult>
{
    public Ema(int period)
        : base(
            (series, price) => series.AddPrice(price),
            quotes => quotes.GetEma(period).LastOrDefault(),
            _ => { })
    {
    }

    protected override void OnUpdate(EmaResult? result)
    {
        Value = IndicatorValueConverter.ToDecimal(result?.Ema);
    }
}
