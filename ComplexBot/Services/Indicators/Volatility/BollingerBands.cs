using ComplexBot.Services.Indicators.MovingAverages;

namespace ComplexBot.Services.Indicators.Volatility;

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

    public IReadOnlyDictionary<string, decimal?> Values => new Dictionary<string, decimal?>
    {
        ["Middle"] = Middle,
        ["Upper"] = Upper,
        ["Lower"] = Lower
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
