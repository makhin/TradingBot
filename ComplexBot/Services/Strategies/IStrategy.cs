using TradingBot.Core.Models;

namespace ComplexBot.Services.Strategies;

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
}
