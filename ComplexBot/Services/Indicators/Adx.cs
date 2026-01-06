using System.Collections.Generic;
using System.Linq;
using ComplexBot.Models;
using Skender.Stock.Indicators;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Average Directional Index - measures trend strength
/// </summary>
public class Adx : IIndicator<Candle>, IMultiValueIndicator
{
    private readonly int _period;
    private readonly QuoteSeries _series = new();

    public Adx(int period = 14)
    {
        _period = period;
    }

    public decimal? Value { get; private set; }
    public decimal? PlusDi { get; private set; }
    public decimal? MinusDi { get; private set; }
    public bool IsReady => Value.HasValue;

    public IReadOnlyDictionary<IndicatorValueKey, decimal?> Values => new Dictionary<IndicatorValueKey, decimal?>
    {
        [IndicatorValueKey.Adx] = Value,
        [IndicatorValueKey.PlusDi] = PlusDi,
        [IndicatorValueKey.MinusDi] = MinusDi
    };

    public decimal? Update(Candle candle)
    {
        _series.AddCandle(candle);

        var result = _series.Quotes.GetAdx(_period).LastOrDefault();
        Value = IndicatorValueConverter.ToDecimal(result?.Adx);
        PlusDi = IndicatorValueConverter.ToDecimal(result?.Pdi);
        MinusDi = IndicatorValueConverter.ToDecimal(result?.Mdi);
        return Value;
    }

    public void Reset()
    {
        _series.Reset();
        Value = null;
        PlusDi = null;
        MinusDi = null;
    }
}
