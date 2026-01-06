using System.Collections.Generic;
using ComplexBot.Models;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Moving Average Convergence Divergence
/// </summary>
public class Macd : IIndicator<decimal>, IMultiValueIndicator
{
    private readonly Ema _fastEma;
    private readonly Ema _slowEma;
    private readonly Ema _signalEma;

    public Macd(int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
    {
        _fastEma = new Ema(fastPeriod);
        _slowEma = new Ema(slowPeriod);
        _signalEma = new Ema(signalPeriod);
    }

    public decimal? Value => MacdLine;
    public decimal? MacdLine { get; private set; }
    public decimal? SignalLine { get; private set; }
    public decimal? Histogram { get; private set; }
    public bool IsReady => _slowEma.IsReady && _signalEma.IsReady;

    public IReadOnlyDictionary<IndicatorValueKey, decimal?> Values => new Dictionary<IndicatorValueKey, decimal?>
    {
        [IndicatorValueKey.MacdLine] = MacdLine,
        [IndicatorValueKey.MacdSignal] = SignalLine,
        [IndicatorValueKey.MacdHistogram] = Histogram
    };

    public decimal? Update(decimal price)
    {
        var fast = _fastEma.Update(price);
        var slow = _slowEma.Update(price);

        if (fast.HasValue && slow.HasValue)
        {
            MacdLine = fast.Value - slow.Value;
            SignalLine = _signalEma.Update(MacdLine.Value);

            if (SignalLine.HasValue)
                Histogram = MacdLine.Value - SignalLine.Value;
        }

        return MacdLine;
    }

    public void Reset()
    {
        _fastEma.Reset();
        _slowEma.Reset();
        _signalEma.Reset();
        MacdLine = null;
        SignalLine = null;
        Histogram = null;
    }
}
