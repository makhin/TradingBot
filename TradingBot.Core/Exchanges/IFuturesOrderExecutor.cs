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

    /// <summary>
    /// Closes an existing position at market price.
    /// Default implementation uses opposite-direction market order (works for one-way mode exchanges).
    /// Exchanges with hedge mode (e.g. Bitget) should override with proper close semantics.
    /// </summary>
    Task<ExecutionResult> ClosePositionAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        CancellationToken ct = default)
    {
        var closeDirection = direction == TradeDirection.Long ? TradeDirection.Short : TradeDirection.Long;
        return PlaceMarketOrderAsync(symbol, closeDirection, quantity, ct);
    }
}
