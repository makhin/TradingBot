using System.Collections.Generic;
using System.Linq;
using ComplexBot.Models;
using Skender.Stock.Indicators;

namespace ComplexBot.Services.Indicators;

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

    public IReadOnlyDictionary<IndicatorValueKey, decimal?> Values => new Dictionary<IndicatorValueKey, decimal?>
    {
        [IndicatorValueKey.Adx] = Value,
        [IndicatorValueKey.PlusDi] = PlusDi,
        [IndicatorValueKey.MinusDi] = MinusDi
    };

    protected override void ResetValues()
    {
        base.ResetValues();
        PlusDi = null;
        MinusDi = null;
    }
}
