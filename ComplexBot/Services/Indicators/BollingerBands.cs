using System.Collections.Generic;
using System.Linq;
using ComplexBot.Models;
using Skender.Stock.Indicators;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Bollinger Bands
/// </summary>
public class BollingerBands : SkenderIndicatorBase<decimal, BollingerBandsResult>, IMultiValueIndicator
{
    public BollingerBands(int period = 20, decimal stdDevMultiplier = 2m)
        : base(
            (series, price) => series.AddPrice(price),
            quotes => quotes.GetBollingerBands(period, (double)stdDevMultiplier).LastOrDefault(),
            _ => { })
    {
    }

    public decimal? Middle => Value;
    public decimal? Upper { get; private set; }
    public decimal? Lower { get; private set; }

    protected override void OnUpdate(BollingerBandsResult? result)
    {
        var middle = IndicatorValueConverter.ToDecimal(result?.Sma);
        Value = middle;
        Upper = IndicatorValueConverter.ToDecimal(result?.UpperBand);
        Lower = IndicatorValueConverter.ToDecimal(result?.LowerBand);
    }

    public IReadOnlyDictionary<IndicatorValueKey, decimal?> Values => new Dictionary<IndicatorValueKey, decimal?>
    {
        [IndicatorValueKey.BollingerMiddle] = Middle,
        [IndicatorValueKey.BollingerUpper] = Upper,
        [IndicatorValueKey.BollingerLower] = Lower
    };

    protected override void ResetValues()
    {
        base.ResetValues();
        Upper = null;
        Lower = null;
    }
}
