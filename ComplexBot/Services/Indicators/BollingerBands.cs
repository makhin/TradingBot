using System;
using System.Collections.Generic;
using System.Linq;
using ComplexBot.Models;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Bollinger Bands
/// </summary>
public class BollingerBands : WindowedIndicator<decimal>, IMultiValueIndicator
{
    private readonly decimal _stdDevMultiplier;

    public BollingerBands(int period = 20, decimal stdDevMultiplier = 2m) : base(period)
    {
        _stdDevMultiplier = stdDevMultiplier;
    }

    public decimal? Middle => CurrentValue;
    public decimal? Upper { get; private set; }
    public decimal? Lower { get; private set; }

    public IReadOnlyDictionary<IndicatorValueKey, decimal?> Values => new Dictionary<IndicatorValueKey, decimal?>
    {
        [IndicatorValueKey.BollingerMiddle] = Middle,
        [IndicatorValueKey.BollingerUpper] = Upper,
        [IndicatorValueKey.BollingerLower] = Lower
    };

    public override decimal? Update(decimal price)
    {
        AddToWindow(price);

        if (!IsReady)
            return null;

        CurrentValue = Window.Average();
        var variance = Window.Average(p => (p - CurrentValue.Value) * (p - CurrentValue.Value));
        var stdDev = (decimal)Math.Sqrt((double)variance);

        Upper = CurrentValue + (_stdDevMultiplier * stdDev);
        Lower = CurrentValue - (_stdDevMultiplier * stdDev);

        return CurrentValue;
    }

    public override void Reset()
    {
        base.Reset();
        Upper = null;
        Lower = null;
    }
}
