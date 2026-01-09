using Binance.Net.Clients;
using Binance.Net.Enums;
using CryptoExchange.Net.Sockets;
using CryptoExchange.Net.Objects.Sockets;
using TradingBot.Core.Models;
using TradingBot.Binance.Common.Interfaces;
using Serilog;
using BinanceKlineInterval = Binance.Net.Enums.KlineInterval;

namespace TradingBot.Binance.Futures;

/// <summary>
/// Binance Futures WebSocket kline/candle listener implementation
/// </summary>
public class FuturesKlineListener : IKlineListener
{
    private readonly BinanceSocketClient _socketClient;
    private readonly ILogger _logger;
    private UpdateSubscription? _currentSubscription;

    public bool IsSubscribed => _currentSubscription != null;

    public FuturesKlineListener(BinanceSocketClient socketClient, ILogger? logger = null)
    {
        _socketClient = socketClient;
        _logger = logger ?? Log.ForContext<FuturesKlineListener>();
    }

    /// <summary>
    /// Subscribes to kline updates for a symbol
    /// </summary>
    public async Task<IDisposable?> SubscribeToKlineUpdatesAsync(
        string symbol,
        TradingBot.Core.Models.KlineInterval interval,
        Action<Candle> onKlineUpdate,
        CancellationToken ct = default)
    {
        var binanceInterval = MapKlineInterval(interval);

        _logger.Information("Subscribing to Futures kline updates: {Symbol} {Interval}", symbol, binanceInterval);

        var result = await _socketClient.UsdFuturesApi.ExchangeData.SubscribeToKlineUpdatesAsync(
            symbol,
            binanceInterval,
            data =>
            {
                var kline = data.Data.Data;

                // Only process closed candles
                if (!kline.Final)
                    return;

                var candle = new Candle(
                    OpenTime: kline.OpenTime,
                    Open: kline.OpenPrice,
                    High: kline.HighPrice,
                    Low: kline.LowPrice,
                    Close: kline.ClosePrice,
                    Volume: kline.Volume,
                    CloseTime: kline.CloseTime
                );

                onKlineUpdate(candle);
            },
            ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to subscribe to Futures klines: {Error}", result.Error?.Message);
            return null;
        }

        _currentSubscription = result.Data;
        _logger.Information("Successfully subscribed to Futures kline updates");

        return new SubscriptionWrapper(result.Data, () =>
        {
            _currentSubscription = null;
            _logger.Information("Unsubscribed from Futures kline updates");
        });
    }

    /// <summary>
    /// Closes all active subscriptions
    /// </summary>
    public async Task UnsubscribeAllAsync()
    {
        if (_currentSubscription != null)
        {
            await _currentSubscription.CloseAsync();
            _currentSubscription = null;
            _logger.Information("Closed all Futures kline subscriptions");
        }
    }

    /// <summary>
    /// Maps TradingBot KlineInterval to Binance KlineInterval
    /// </summary>
    private static BinanceKlineInterval MapKlineInterval(TradingBot.Core.Models.KlineInterval interval)
    {
        return interval switch
        {
            TradingBot.Core.Models.KlineInterval.OneMinute => BinanceKlineInterval.OneMinute,
            TradingBot.Core.Models.KlineInterval.FiveMinutes => BinanceKlineInterval.FiveMinutes,
            TradingBot.Core.Models.KlineInterval.FifteenMinutes => BinanceKlineInterval.FifteenMinutes,
            TradingBot.Core.Models.KlineInterval.ThirtyMinutes => BinanceKlineInterval.ThirtyMinutes,
            TradingBot.Core.Models.KlineInterval.OneHour => BinanceKlineInterval.OneHour,
            TradingBot.Core.Models.KlineInterval.FourHour => BinanceKlineInterval.FourHour,
            TradingBot.Core.Models.KlineInterval.OneDay => BinanceKlineInterval.OneDay,
            TradingBot.Core.Models.KlineInterval.OneWeek => BinanceKlineInterval.OneWeek,
            _ => throw new ArgumentException($"Unsupported interval: {interval}")
        };
    }

    /// <summary>
    /// Wrapper for UpdateSubscription that implements IDisposable
    /// </summary>
    private class SubscriptionWrapper : IDisposable
    {
        private readonly UpdateSubscription _subscription;
        private readonly Action _onDispose;

        public SubscriptionWrapper(UpdateSubscription subscription, Action onDispose)
        {
            _subscription = subscription;
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            _subscription.CloseAsync().GetAwaiter().GetResult();
            _onDispose();
        }
    }
}
