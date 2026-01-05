using System;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Base class for EMA-style indicators using exponential smoothing
/// </summary>
public abstract class ExponentialIndicator<TInput> : IIndicator<TInput>
{
    protected readonly int Period;
    protected readonly decimal Multiplier;
    protected decimal? CurrentValue;
    protected int DataCount;

    protected ExponentialIndicator(int period)
    {
        if (period <= 0)
            throw new ArgumentOutOfRangeException(nameof(period), "Period must be positive");
        Period = period;
        Multiplier = 2m / (period + 1);
    }

    public decimal? Value => CurrentValue;
    public bool IsReady => DataCount >= Period;

    public abstract decimal? Update(TInput input);

    public virtual void Reset()
    {
        CurrentValue = null;
        DataCount = 0;
    }

    /// <summary>
    /// Applies exponential smoothing to a new value
    /// </summary>
    protected decimal Smooth(decimal newValue)
    {
        DataCount++;
        CurrentValue = CurrentValue == null
            ? newValue
            : (newValue - CurrentValue.Value) * Multiplier + CurrentValue.Value;
        return CurrentValue.Value;
    }
}
