using System.Linq;
using ComplexBot.Models;
using Skender.Stock.Indicators;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// On-Balance Volume - confirms trend strength via volume
/// </summary>
public class Obv : IIndicator<Candle>
{
    private readonly Sma _obvSma;
    private readonly QuoteSeries _series = new();

    public Obv(int signalPeriod = 20)
    {
        _obvSma = new Sma(signalPeriod);
    }

    public decimal? Value { get; private set; }
    public decimal? Signal => _obvSma.Value;
    public bool IsReady => _obvSma.IsReady && Value.HasValue;

    public bool IsBullish => _obvSma.Value.HasValue && Value.HasValue && Value.Value > _obvSma.Value;
    public bool IsBearish => _obvSma.Value.HasValue && Value.HasValue && Value.Value < _obvSma.Value;

    public decimal? Update(Candle candle)
    {
        _series.AddCandle(candle);

        var result = _series.Quotes.GetObv().LastOrDefault();
        Value = IndicatorValueConverter.ToDecimal(result?.Obv);
        if (Value.HasValue)
            _obvSma.Update(Value.Value);

        return Value;
    }

    public void Reset()
    {
        _series.Reset();
        Value = null;
        _obvSma.Reset();
    }
}
