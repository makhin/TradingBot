using TradingBot.Core.Models;

namespace TradingBot.Binance.Common.Interfaces;

/// <summary>
/// Interface for subscribing to real-time kline/candle updates via WebSocket
/// </summary>
public interface IKlineListener
{
    /// <summary>
    /// Subscribes to kline updates for a symbol
    /// </summary>
    /// <param name="symbol">Trading symbol (e.g., BTCUSDT)</param>
    /// <param name="interval">Kline interval</param>
    /// <param name="onKlineUpdate">Callback invoked when a new kline is received</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Subscription object that can be used to unsubscribe</returns>
    Task<IDisposable?> SubscribeToKlineUpdatesAsync(
        string symbol,
        KlineInterval interval,
        Action<Candle> onKlineUpdate,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if currently subscribed and connected
    /// </summary>
    bool IsSubscribed { get; }

    /// <summary>
    /// Closes all active subscriptions
    /// </summary>
    Task UnsubscribeAllAsync();
}
