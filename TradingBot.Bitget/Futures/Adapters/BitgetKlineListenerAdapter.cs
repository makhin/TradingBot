using TradingBot.Core.Exchanges;
using TradingBot.Core.Models;

namespace TradingBot.Bitget.Futures.Adapters;

/// <summary>
/// Adapter that wraps Bitget BitgetKlineListener and exposes generic IExchangeKlineListener interface
/// </summary>
public class BitgetKlineListenerAdapter : IExchangeKlineListener
{
    private readonly BitgetKlineListener _bitgetListener;

    public bool IsSubscribed => _bitgetListener.IsSubscribed;

    public BitgetKlineListenerAdapter(BitgetKlineListener bitgetListener)
    {
        _bitgetListener = bitgetListener;
    }

    public Task<IDisposable?> SubscribeToKlineUpdatesAsync(
        string symbol,
        KlineInterval interval,
        Action<Candle> onKlineUpdate,
        CancellationToken ct = default)
    {
        return _bitgetListener.SubscribeToKlineUpdatesAsync(symbol, interval, onKlineUpdate, ct);
    }

    public Task UnsubscribeAllAsync()
        => _bitgetListener.UnsubscribeAllAsync();
}
