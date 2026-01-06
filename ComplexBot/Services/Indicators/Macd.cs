using System.Collections.Generic;
using System.Linq;
using ComplexBot.Models;
using Skender.Stock.Indicators;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Moving Average Convergence Divergence
/// </summary>
public class Macd : IIndicator<decimal>, IMultiValueIndicator
{
    private readonly int _fastPeriod;
    private readonly int _slowPeriod;
    private readonly int _signalPeriod;
    private readonly QuoteSeries _series = new();

    public Macd(int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
    {
        _fastPeriod = fastPeriod;
        _slowPeriod = slowPeriod;
        _signalPeriod = signalPeriod;
    }

    public decimal? Value => MacdLine;
    public decimal? MacdLine { get; private set; }
    public decimal? SignalLine { get; private set; }
    public decimal? Histogram { get; private set; }
    public bool IsReady => MacdLine.HasValue && SignalLine.HasValue;

    public IReadOnlyDictionary<IndicatorValueKey, decimal?> Values => new Dictionary<IndicatorValueKey, decimal?>
    {
        [IndicatorValueKey.MacdLine] = MacdLine,
        [IndicatorValueKey.MacdSignal] = SignalLine,
        [IndicatorValueKey.MacdHistogram] = Histogram
    };

    public decimal? Update(decimal price)
    {
        _series.AddPrice(price);

        var result = _series.Quotes.GetMacd(_fastPeriod, _slowPeriod, _signalPeriod).LastOrDefault();
        MacdLine = IndicatorValueConverter.ToDecimal(result?.Macd);
        SignalLine = IndicatorValueConverter.ToDecimal(result?.Signal);
        Histogram = IndicatorValueConverter.ToDecimal(result?.Histogram);

        return MacdLine;
    }

    public void Reset()
    {
        _series.Reset();
        MacdLine = null;
        SignalLine = null;
        Histogram = null;
    }
}
