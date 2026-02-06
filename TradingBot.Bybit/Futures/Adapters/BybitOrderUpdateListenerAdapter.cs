using TradingBot.Core.Exchanges;
using TradingBot.Core.Models;

namespace TradingBot.Bybit.Futures.Adapters;

/// <summary>
/// Adapter that wraps BybitOrderUpdateListener and exposes generic IExchangeOrderUpdateListener interface
/// Enables exchange-agnostic WebSocket order monitoring while preserving Bybit implementation
/// </summary>
public class BybitOrderUpdateListenerAdapter : IExchangeOrderUpdateListener
{
    private readonly BybitOrderUpdateListener _bybitListener;

    public bool IsSubscribed => _bybitListener.IsSubscribed;

    public BybitOrderUpdateListenerAdapter(BybitOrderUpdateListener bybitListener)
    {
        _bybitListener = bybitListener;
    }

    public Task<IDisposable?> SubscribeToOrderUpdatesAsync(
        Action<OrderUpdate> onOrderUpdate,
        CancellationToken ct = default)
        => _bybitListener.SubscribeToOrderUpdatesAsync(onOrderUpdate, ct);

    public Task<IDisposable?> SubscribeToPositionUpdatesAsync(
        Action<PositionUpdate> onPositionUpdate,
        CancellationToken ct = default)
        => _bybitListener.SubscribeToPositionUpdatesAsync(onPositionUpdate, ct);

    public Task<IDisposable?> SubscribeToAccountUpdatesAsync(
        Action<AccountUpdate> onAccountUpdate,
        CancellationToken ct = default)
        => _bybitListener.SubscribeToAccountUpdatesAsync(onAccountUpdate, ct);

    public Task UnsubscribeAllAsync()
        => _bybitListener.UnsubscribeAllAsync();
}
