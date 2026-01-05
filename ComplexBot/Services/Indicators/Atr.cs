using System;
using System.Linq;
using ComplexBot.Models;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Average True Range - measures volatility
/// </summary>
public class Atr : WindowedIndicator<Candle>
{
    private decimal? _previousClose;

    public Atr(int period = 14) : base(period) { }

    public override decimal? Update(Candle candle)
    {
        decimal trueRange = CalculateTrueRange(candle);
        AddToWindow(trueRange);
        _previousClose = candle.Close;

        if (IsReady)
        {
            CurrentValue = Window.Average();
            return CurrentValue;
        }
        return null;
    }

    private decimal CalculateTrueRange(Candle candle)
    {
        if (_previousClose == null)
            return candle.High - candle.Low;

        return Math.Max(
            candle.High - candle.Low,
            Math.Max(
                Math.Abs(candle.High - _previousClose.Value),
                Math.Abs(candle.Low - _previousClose.Value)
            )
        );
    }

    public override void Reset()
    {
        base.Reset();
        _previousClose = null;
    }
}
