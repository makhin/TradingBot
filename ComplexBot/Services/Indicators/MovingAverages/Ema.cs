namespace ComplexBot.Services.Indicators.MovingAverages;

/// <summary>
/// Exponential Moving Average
/// </summary>
public class Ema : ExponentialIndicator<decimal>
{
    public Ema(int period) : base(period) { }

    public override decimal? Update(decimal price)
    {
        Smooth(price);
        return IsReady ? CurrentValue : null;
    }
}
