namespace TradingBot.Core.Models;

/// <summary>
/// Base interface for executing orders on any exchange
/// </summary>
public interface IOrderExecutor
{
    /// <summary>
    /// Places a market order
    /// </summary>
    Task<ExecutionResult> PlaceMarketOrderAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        CancellationToken ct = default);

    /// <summary>
    /// Places a limit order
    /// </summary>
    Task<ExecutionResult> PlaceLimitOrderAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal price,
        CancellationToken ct = default);
}
