using TradingBot.Core.Models;

namespace TradingBot.Binance.Common.Interfaces;

/// <summary>
/// Interface for subscribing to order and position updates via User Data Stream
/// </summary>
public interface IOrderUpdateListener
{
    /// <summary>
    /// Subscribes to order execution updates
    /// </summary>
    /// <param name="onOrderUpdate">Callback when an order is updated or filled</param>
    /// <param name="ct">Cancellation token</param>
    Task<IDisposable?> SubscribeToOrderUpdatesAsync(
        Action<OrderUpdate> onOrderUpdate,
        CancellationToken ct = default);

    /// <summary>
    /// Subscribes to position updates (Futures only)
    /// </summary>
    /// <param name="onPositionUpdate">Callback when a position is updated</param>
    /// <param name="ct">Cancellation token</param>
    Task<IDisposable?> SubscribeToPositionUpdatesAsync(
        Action<PositionUpdate> onPositionUpdate,
        CancellationToken ct = default);

    /// <summary>
    /// Subscribes to account balance updates
    /// </summary>
    /// <param name="onAccountUpdate">Callback when account balance changes</param>
    /// <param name="ct">Cancellation token</param>
    Task<IDisposable?> SubscribeToAccountUpdatesAsync(
        Action<AccountUpdate> onAccountUpdate,
        CancellationToken ct = default);

    /// <summary>
    /// Gets whether the listener is currently subscribed
    /// </summary>
    bool IsSubscribed { get; }

    /// <summary>
    /// Closes all active subscriptions
    /// </summary>
    Task UnsubscribeAllAsync();
}

/// <summary>
/// Order execution update event
/// </summary>
public record OrderUpdate
{
    public required string Symbol { get; init; }
    public required long OrderId { get; init; }
    public required string Status { get; init; }
    public required TradeDirection Direction { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal Price { get; init; }
    public required decimal AveragePrice { get; init; }
    public required decimal QuantityFilled { get; init; }
    public required DateTime UpdateTime { get; init; }
    public string? OrderType { get; init; }
    public string? TimeInForce { get; init; }
}

/// <summary>
/// Position update event (Futures only)
/// </summary>
public record PositionUpdate
{
    public required string Symbol { get; init; }
    public required decimal PositionAmount { get; init; }
    public required decimal EntryPrice { get; init; }
    public required decimal UnrealizedPnl { get; init; }
    public required DateTime UpdateTime { get; init; }
    public TradeDirection? Side { get; init; }
}

/// <summary>
/// Account balance update event
/// </summary>
public record AccountUpdate
{
    public required string Asset { get; init; }
    public required decimal Balance { get; init; }
    public required decimal AvailableBalance { get; init; }
    public required DateTime UpdateTime { get; init; }
}
