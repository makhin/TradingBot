using System.Collections.Generic;
using TradingBot.Indicators.Base;
using TradingBot.Indicators.Utils;
using System.Linq;
using TradingBot.Core.Models;
using Skender.Stock.Indicators;

namespace TradingBot.Indicators.Trend;

/// <summary>
/// Average Directional Index - measures trend strength
/// </summary>
public class Adx : SkenderIndicatorBase<Candle, AdxResult>, IMultiValueIndicator
{
    public Adx(int period = 14)
        : base(
            (series, candle) => series.AddCandle(candle),
            quotes => quotes.GetAdx(period).LastOrDefault(),
            _ => { })
    {
    }

    public decimal? PlusDi { get; private set; }
    public decimal? MinusDi { get; private set; }

    protected override void OnUpdate(AdxResult? result)
    {
        Value = IndicatorValueConverter.ToDecimal(result?.Adx);
        PlusDi = IndicatorValueConverter.ToDecimal(result?.Pdi);
        MinusDi = IndicatorValueConverter.ToDecimal(result?.Mdi);
    }

    public IReadOnlyDictionary<string, decimal?> Values => new Dictionary<string, decimal?>
    {
        ["ADX"] = Value,
        ["+DI"] = PlusDi,
        ["-DI"] = MinusDi
    };

    protected override void ResetValues()
    {
        base.ResetValues();
        PlusDi = null;
        MinusDi = null;
    }
}
