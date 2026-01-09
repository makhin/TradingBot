using System.Linq;
using TradingBot.Indicators.Base;
using TradingBot.Indicators.Trend;
using TradingBot.Indicators.Utils;
using TradingBot.Core.Models;
using Skender.Stock.Indicators;

namespace TradingBot.Indicators.Volume;

/// <summary>
/// On-Balance Volume - confirms trend strength via volume
/// </summary>
public class Obv : SkenderIndicatorBase<Candle, ObvResult>
{
    private readonly Sma _obvSma;

    public Obv(int signalPeriod = 20)
        : base(
            (series, candle) => series.AddCandle(candle),
            quotes => quotes.GetObv().LastOrDefault(),
            _ => { })
    {
        _obvSma = new Sma(signalPeriod);
    }

    public decimal? Signal => _obvSma.Value;
    public override bool IsReady => _obvSma.IsReady && Value.HasValue;

    public bool IsBullish => _obvSma.Value.HasValue && Value.HasValue && Value.Value > _obvSma.Value;
    public bool IsBearish => _obvSma.Value.HasValue && Value.HasValue && Value.Value < _obvSma.Value;

    protected override void OnUpdate(ObvResult? result)
    {
        Value = IndicatorValueConverter.ToDecimal(result?.Obv);
        if (Value.HasValue)
            _obvSma.Update(Value.Value);
    }

    protected override void ResetValues()
    {
        base.ResetValues();
        _obvSma.Reset();
    }
}
