using System.Collections.Generic;
using System.Linq;
using ComplexBot.Models;
using Skender.Stock.Indicators;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Bollinger Bands
/// </summary>
public class BollingerBands : IIndicator<decimal>, IMultiValueIndicator
{
    private readonly int _period;
    private readonly decimal _stdDevMultiplier;
    private readonly QuoteSeries _series = new();

    public BollingerBands(int period = 20, decimal stdDevMultiplier = 2m)
    {
        _period = period;
        _stdDevMultiplier = stdDevMultiplier;
    }

    public decimal? Middle => Value;
    public decimal? Upper { get; private set; }
    public decimal? Lower { get; private set; }
    public decimal? Value { get; private set; }
    public bool IsReady => Value.HasValue;

    public IReadOnlyDictionary<IndicatorValueKey, decimal?> Values => new Dictionary<IndicatorValueKey, decimal?>
    {
        [IndicatorValueKey.BollingerMiddle] = Middle,
        [IndicatorValueKey.BollingerUpper] = Upper,
        [IndicatorValueKey.BollingerLower] = Lower
    };

    public decimal? Update(decimal price)
    {
        _series.AddPrice(price);

        var result = _series.Quotes.GetBollingerBands(_period, (double)_stdDevMultiplier).LastOrDefault();
        Value = IndicatorValueConverter.ToDecimal(result?.Sma);
        Upper = IndicatorValueConverter.ToDecimal(result?.UpperBand);
        Lower = IndicatorValueConverter.ToDecimal(result?.LowerBand);
        return Value;
    }

    public void Reset()
    {
        _series.Reset();
        Value = null;
        Upper = null;
        Lower = null;
    }
}
