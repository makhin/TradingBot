using TradingBot.Core.Models;

namespace TradingBot.Core.Exchanges;

/// <summary>
/// Interface for subscribing to order and position updates via User Data Stream
/// Exchange-agnostic version that works across different exchanges
/// </summary>
public interface IExchangeOrderUpdateListener
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
