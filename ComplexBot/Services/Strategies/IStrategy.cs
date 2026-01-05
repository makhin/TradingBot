using ComplexBot.Models;

namespace ComplexBot.Services.Strategies;

/// <summary>
/// Base interface for all trading strategies.
/// Strategies analyze candles and generate trading signals.
/// </summary>
public interface IStrategy
{
    string Name { get; }
    TradeSignal? Analyze(Candle candle, decimal? currentPosition, string symbol);
    void Reset();

    /// <summary>
    /// Current stop loss level (trailing stop if applicable).
    /// Used by BacktestEngine to sync with PositionState.
    /// </summary>
    decimal? CurrentStopLoss { get; }

    /// <summary>
    /// Current ATR value for volatility-based position sizing.
    /// Used by RiskManager to adjust position size.
    /// </summary>
    decimal? CurrentAtr { get; }

    /// <summary>
    /// Gets the current state of the strategy for multi-timeframe filtering.
    /// Returns indicator values and market conditions that filters can use.
    /// </summary>
    StrategyState GetCurrentState();
}
