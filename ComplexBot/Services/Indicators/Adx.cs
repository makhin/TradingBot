using System;
using System.Collections.Generic;
using ComplexBot.Models;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Average Directional Index - measures trend strength
/// </summary>
public class Adx : IIndicator<Candle>, IMultiValueIndicator
{
    private readonly int _period;
    private readonly Ema _smoothedPlusDm;
    private readonly Ema _smoothedMinusDm;
    private readonly Ema _smoothedTr;
    private readonly Ema _adxEma;
    private decimal? _previousHigh;
    private decimal? _previousLow;
    private decimal? _previousClose;

    public Adx(int period = 14)
    {
        _period = period;
        _smoothedPlusDm = new Ema(period);
        _smoothedMinusDm = new Ema(period);
        _smoothedTr = new Ema(period);
        _adxEma = new Ema(period);
    }

    public decimal? Value { get; private set; }
    public decimal? PlusDi { get; private set; }
    public decimal? MinusDi { get; private set; }
    public bool IsReady => Value.HasValue;

    public IReadOnlyDictionary<string, decimal?> Values => new Dictionary<string, decimal?>
    {
        ["ADX"] = Value,
        ["+DI"] = PlusDi,
        ["-DI"] = MinusDi
    };

    public decimal? Update(Candle candle)
    {
        if (_previousHigh == null)
        {
            _previousHigh = candle.High;
            _previousLow = candle.Low;
            _previousClose = candle.Close;
            return null;
        }

        // Calculate directional movement
        decimal upMove = candle.High - _previousHigh!.Value;
        decimal downMove = _previousLow!.Value - candle.Low;

        decimal plusDm = (upMove > downMove && upMove > 0) ? upMove : 0;
        decimal minusDm = (downMove > upMove && downMove > 0) ? downMove : 0;

        // Calculate True Range
        decimal tr = CalculateTrueRange(candle);

        // Smooth the values
        var smoothedTr = _smoothedTr.Update(tr);
        var smoothedPlusDm = _smoothedPlusDm.Update(plusDm);
        var smoothedMinusDm = _smoothedMinusDm.Update(minusDm);

        _previousHigh = candle.High;
        _previousLow = candle.Low;
        _previousClose = candle.Close;

        if (smoothedTr > 0 && _smoothedTr.IsReady)
        {
            PlusDi = 100 * smoothedPlusDm / smoothedTr;
            MinusDi = 100 * smoothedMinusDm / smoothedTr;

            decimal diSum = PlusDi!.Value + MinusDi!.Value;
            if (diSum > 0)
            {
                decimal dx = 100 * Math.Abs(PlusDi.Value - MinusDi.Value) / diSum;
                Value = _adxEma.Update(dx);
            }
        }

        return Value;
    }

    private decimal CalculateTrueRange(Candle candle)
    {
        return Math.Max(
            candle.High - candle.Low,
            Math.Max(
                Math.Abs(candle.High - _previousClose!.Value),
                Math.Abs(candle.Low - _previousClose.Value)
            )
        );
    }

    public void Reset()
    {
        _smoothedPlusDm.Reset();
        _smoothedMinusDm.Reset();
        _smoothedTr.Reset();
        _adxEma.Reset();
        _previousHigh = null;
        _previousLow = null;
        _previousClose = null;
        Value = null;
        PlusDi = null;
        MinusDi = null;
    }
}
