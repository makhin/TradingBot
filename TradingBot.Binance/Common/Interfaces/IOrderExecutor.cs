using TradingBot.Core.Models;
using BinanceExecutionResult = TradingBot.Binance.Common.Models.ExecutionResult;

namespace TradingBot.Binance.Common.Interfaces;

/// <summary>
/// Interface for executing orders on Binance
/// </summary>
public interface IOrderExecutor
{
    /// <summary>
    /// Places a market order
    /// </summary>
    Task<BinanceExecutionResult> PlaceMarketOrderAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        CancellationToken ct = default);

    /// <summary>
    /// Places a limit order
    /// </summary>
    Task<BinanceExecutionResult> PlaceLimitOrderAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal price,
        CancellationToken ct = default);

    /// <summary>
    /// Places an OCO (One-Cancels-Other) order with stop-loss and take-profit
    /// </summary>
    Task<BinanceExecutionResult> PlaceOcoOrderAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal stopLossPrice,
        decimal stopLimitPrice,
        decimal takeProfitPrice,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels all orders for a symbol
    /// </summary>
    Task<bool> CancelAllOrdersAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// Cancels OCO order list
    /// </summary>
    Task<bool> CancelOcoOrderAsync(string symbol, long orderListId, CancellationToken ct = default);
}
