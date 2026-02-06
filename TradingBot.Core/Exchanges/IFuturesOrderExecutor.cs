using TradingBot.Core.Models;

namespace TradingBot.Core.Exchanges;

/// <summary>
/// Extended order executor interface for Futures exchanges with stop-loss and take-profit methods
/// </summary>
public interface IFuturesOrderExecutor : IOrderExecutor
{
    /// <summary>
    /// Places a stop-loss order (Stop Market)
    /// </summary>
    Task<ExecutionResult> PlaceStopLossAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal stopPrice,
        CancellationToken ct = default);

    /// <summary>
    /// Places a take-profit order (Take Profit Market)
    /// </summary>
    Task<ExecutionResult> PlaceTakeProfitAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal takeProfitPrice,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels a specific order
    /// </summary>
    Task<bool> CancelOrderAsync(string symbol, long orderId, CancellationToken ct = default);
}
