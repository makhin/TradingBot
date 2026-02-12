using TradingBot.Core.Models;
using BitgetExecutionResult = TradingBot.Bitget.Futures.Models.ExecutionResult;

namespace TradingBot.Bitget.Futures.Interfaces;

/// <summary>
/// Interface for Bitget Futures order execution
/// </summary>
public interface IBitgetFuturesOrderExecutor
{
    /// <summary>
    /// Places a market order to open a position
    /// </summary>
    Task<BitgetExecutionResult> PlaceMarketOrderAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        CancellationToken ct = default);

    /// <summary>
    /// Closes an existing position at market price.
    /// Uses correct hedge-mode semantics (side=position direction + tradeSide=Close).
    /// </summary>
    Task<BitgetExecutionResult> ClosePositionAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        CancellationToken ct = default);

    /// <summary>
    /// Places a stop-loss order
    /// </summary>
    Task<BitgetExecutionResult> PlaceStopLossAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal stopPrice,
        CancellationToken ct = default);

    /// <summary>
    /// Places a take-profit order
    /// </summary>
    Task<BitgetExecutionResult> PlaceTakeProfitAsync(
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
    /// Cancels all open orders for a symbol
    /// </summary>
    Task<bool> CancelAllOrdersAsync(string symbol, CancellationToken ct = default);
}
