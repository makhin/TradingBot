using System.Collections.Generic;
using System.Linq;
using ComplexBot.Models;
using Skender.Stock.Indicators;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Moving Average Convergence Divergence
/// </summary>
public class Macd : SkenderIndicatorBase<decimal, MacdResult>, IMultiValueIndicator
{
    public Macd(int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
        : base(
            (series, price) => series.AddPrice(price),
            quotes => quotes.GetMacd(fastPeriod, slowPeriod, signalPeriod).LastOrDefault(),
            _ => { })
    {
    }

    public decimal? MacdLine { get; private set; }
    public decimal? SignalLine { get; private set; }
    public decimal? Histogram { get; private set; }
    public override bool IsReady => MacdLine.HasValue && SignalLine.HasValue;

    protected override void OnUpdate(MacdResult? result)
    {
        MacdLine = IndicatorValueConverter.ToDecimal(result?.Macd);
        SignalLine = IndicatorValueConverter.ToDecimal(result?.Signal);
        Histogram = IndicatorValueConverter.ToDecimal(result?.Histogram);
        Value = MacdLine;
    }

    public IReadOnlyDictionary<IndicatorValueKey, decimal?> Values => new Dictionary<IndicatorValueKey, decimal?>
    {
        [IndicatorValueKey.MacdLine] = MacdLine,
        [IndicatorValueKey.MacdSignal] = SignalLine,
        [IndicatorValueKey.MacdHistogram] = Histogram
    };

    protected override void ResetValues()
    {
        base.ResetValues();
        MacdLine = null;
        SignalLine = null;
        Histogram = null;
    }
}
