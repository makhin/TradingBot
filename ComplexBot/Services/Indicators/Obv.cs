using System.Linq;
using ComplexBot.Models;
using Skender.Stock.Indicators;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// On-Balance Volume - confirms trend strength via volume
/// </summary>
public class Obv : SkenderIndicatorBase<Candle, ObvResult>
{
    private Sma _obvSma = new(1);

    public Obv(int signalPeriod = 20)
        : base(
            (series, candle) => series.AddCandle(candle),
            quotes => quotes.GetObv().LastOrDefault(),
            result =>
            {
                Value = IndicatorValueConverter.ToDecimal(result?.Obv);
                if (Value.HasValue)
                    _obvSma.Update(Value.Value);
            })
    {
        _obvSma = new Sma(signalPeriod);
    }

    public decimal? Signal => _obvSma.Value;
    public override bool IsReady => _obvSma.IsReady && Value.HasValue;

    public bool IsBullish => _obvSma.Value.HasValue && Value.HasValue && Value.Value > _obvSma.Value;
    public bool IsBearish => _obvSma.Value.HasValue && Value.HasValue && Value.Value < _obvSma.Value;

    protected override void ResetValues()
    {
        base.ResetValues();
        _obvSma.Reset();
    }
}
