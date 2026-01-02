namespace ComplexBot.Services.Indicators;

/// <summary>
/// Base class for indicators that use a sliding window of values
/// </summary>
/// <typeparam name="TInput">Type of input data</typeparam>
public abstract class WindowedIndicator<TInput> : IIndicator<TInput>
{
    protected readonly int Period;
    protected readonly Queue<decimal> Window = new();
    protected decimal? CurrentValue;

    protected WindowedIndicator(int period)
    {
        if (period <= 0)
            throw new ArgumentOutOfRangeException(nameof(period), "Period must be positive");
        Period = period;
    }

    public decimal? Value => CurrentValue;
    public virtual bool IsReady => Window.Count >= Period;

    public abstract decimal? Update(TInput input);

    public virtual void Reset()
    {
        Window.Clear();
        CurrentValue = null;
    }

    /// <summary>
    /// Adds a value to the window, maintaining the period size
    /// </summary>
    protected void AddToWindow(decimal value)
    {
        Window.Enqueue(value);
        if (Window.Count > Period)
            Window.Dequeue();
    }
}

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

/// <summary>
/// Helper class for calculating PnL
/// </summary>
public static class PnLCalculator
{
    /// <summary>
    /// Calculates profit/loss for a position
    /// </summary>
    public static decimal Calculate(decimal entryPrice, decimal exitPrice, decimal quantity, bool isLong)
    {
        return isLong
            ? (exitPrice - entryPrice) * quantity
            : (entryPrice - exitPrice) * quantity;
    }

    /// <summary>
    /// Calculates profit/loss with fees
    /// </summary>
    public static decimal CalculateNet(
        decimal entryPrice,
        decimal exitPrice,
        decimal quantity,
        bool isLong,
        decimal feePercent)
    {
        decimal grossPnl = Calculate(entryPrice, exitPrice, quantity, isLong);
        decimal entryFee = entryPrice * quantity * feePercent / 100;
        decimal exitFee = exitPrice * quantity * feePercent / 100;
        return grossPnl - entryFee - exitFee;
    }
}
