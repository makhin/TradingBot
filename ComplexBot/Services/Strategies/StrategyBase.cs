using ComplexBot.Models;

namespace ComplexBot.Services.Strategies;

public abstract class StrategyBase<TSettings> : IStrategy
    where TSettings : class, new()
{
    protected StrategyBase(TSettings? settings = null)
    {
        Settings = settings ?? new TSettings();
    }

    public abstract string Name { get; }
    public virtual decimal? CurrentStopLoss => null;
    public virtual decimal? CurrentAtr => null;
    public virtual decimal? PrimaryIndicatorValue => GetCurrentState().IndicatorValue;

    protected TSettings Settings { get; }

    /// <summary>
    /// Gets the current state of the strategy for multi-timeframe filtering.
    /// Default implementation returns empty state - derived strategies should override.
    /// </summary>
    public virtual StrategyState GetCurrentState() => StrategyState.Empty;

    public TradeSignal? Analyze(Candle candle, decimal? currentPosition, string symbol)
    {
        UpdateIndicators(candle);

        if (!IndicatorsReady)
        {
            OnIndicatorsNotReady();
            return null;
        }

        bool hasPosition = currentPosition.HasValue && currentPosition.Value != 0;

        if (hasPosition)
        {
            var exitSignal = CheckExitConditions(candle, currentPosition!.Value, symbol);
            if (exitSignal != null)
            {
                AfterSignal(exitSignal);
                return exitSignal;
            }
        }

        if (!hasPosition)
        {
            var entrySignal = CheckEntryConditions(candle, symbol);
            if (entrySignal != null)
            {
                AfterSignal(entrySignal);
                return entrySignal;
            }
        }

        AfterNoSignal();
        return null;
    }

    protected abstract void UpdateIndicators(Candle candle);
    protected abstract bool IndicatorsReady { get; }
    protected abstract TradeSignal? CheckEntryConditions(Candle candle, string symbol);
    protected abstract TradeSignal? CheckExitConditions(Candle candle, decimal position, string symbol);

    protected virtual void OnIndicatorsNotReady() { }
    protected virtual void AfterSignal(TradeSignal signal) { }
    protected virtual void AfterNoSignal() { }

    public abstract void Reset();
}
