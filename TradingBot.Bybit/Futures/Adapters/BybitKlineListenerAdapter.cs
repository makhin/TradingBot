using TradingBot.Core.Exchanges;
using TradingBot.Core.Models;

namespace TradingBot.Bybit.Futures.Adapters;

/// <summary>
/// Adapter that wraps BybitKlineListener and exposes generic IExchangeKlineListener interface
/// Enables exchange-agnostic WebSocket kline monitoring while preserving Bybit implementation
/// </summary>
public class BybitKlineListenerAdapter : IExchangeKlineListener
{
    private readonly BybitKlineListener _bybitListener;

    public bool IsSubscribed => _bybitListener.IsSubscribed;

    public BybitKlineListenerAdapter(BybitKlineListener bybitListener)
    {
        _bybitListener = bybitListener;
    }

    public Task<IDisposable?> SubscribeToKlineUpdatesAsync(
        string symbol,
        KlineInterval interval,
        Action<Candle> onKlineUpdate,
        CancellationToken ct = default)
        => _bybitListener.SubscribeToKlineUpdatesAsync(symbol, interval, onKlineUpdate, ct);

    public Task UnsubscribeAllAsync()
        => _bybitListener.UnsubscribeAllAsync();
}
