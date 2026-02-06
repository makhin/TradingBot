using TradingBot.Core.Exchanges;
using TradingBot.Core.Models;
using TradingBot.Binance.Common.Interfaces;

namespace TradingBot.Binance.Futures.Adapters;

/// <summary>
/// Adapter that wraps Binance IKlineListener and exposes generic IExchangeKlineListener interface
/// Enables exchange-agnostic WebSocket kline monitoring while preserving existing Binance implementation
/// </summary>
public class BinanceKlineListenerAdapter : IExchangeKlineListener
{
    private readonly IKlineListener _binanceListener;

    public bool IsSubscribed => _binanceListener.IsSubscribed;

    public BinanceKlineListenerAdapter(IKlineListener binanceListener)
    {
        _binanceListener = binanceListener;
    }

    // Delegate all calls to the underlying Binance listener
    public Task<IDisposable?> SubscribeToKlineUpdatesAsync(
        string symbol,
        KlineInterval interval,
        Action<Candle> onKlineUpdate,
        CancellationToken ct = default)
        => _binanceListener.SubscribeToKlineUpdatesAsync(symbol, interval, onKlineUpdate, ct);

    public Task UnsubscribeAllAsync()
        => _binanceListener.UnsubscribeAllAsync();
}
