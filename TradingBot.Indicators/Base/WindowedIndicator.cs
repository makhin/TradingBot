using System;
using TradingBot.Indicators.Utils;
using System.Collections.Generic;

namespace TradingBot.Indicators.Base;

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
