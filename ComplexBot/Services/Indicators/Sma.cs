using System.Linq;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Simple Moving Average
/// </summary>
public class Sma : WindowedIndicator<decimal>
{
    public Sma(int period) : base(period) { }

    public override decimal? Update(decimal price)
    {
        AddToWindow(price);

        if (IsReady)
        {
            CurrentValue = Window.Average();
            return CurrentValue;
        }
        return null;
    }
}
