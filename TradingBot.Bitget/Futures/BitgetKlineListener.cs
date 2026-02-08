using Bitget.Net.Clients;
using Bitget.Net.Enums;
using Bitget.Net.Enums.V2;
using CryptoExchange.Net.Sockets;
using CryptoExchange.Net.Objects.Sockets;
using TradingBot.Core.Models;
using TradingBot.Bitget.Common;
using Serilog;
using CoreKlineInterval = TradingBot.Core.Models.KlineInterval;

namespace TradingBot.Bitget.Futures;

/// <summary>
/// Bitget Futures Kline/Candlestick WebSocket listener using JK.Bitget.Net v3.4.0
/// </summary>
public class BitgetKlineListener
{
    private readonly BitgetSocketClient _socketClient;
    private readonly ILogger _logger;
    private UpdateSubscription? _klineSubscription;

    public bool IsSubscribed => _klineSubscription != null;

    public BitgetKlineListener(BitgetSocketClient socketClient, ILogger? logger = null)
    {
        _socketClient = socketClient;
        _logger = logger ?? Log.ForContext<BitgetKlineListener>();
    }

    public async Task<IDisposable?> SubscribeToKlineUpdatesAsync(
        string symbol,
        CoreKlineInterval interval,
        Action<Candle> onKlineUpdate,
        CancellationToken ct = default)
    {
        var intervalBitget = BitgetHelpers.MapStreamKlineInterval(interval);

        _logger.Information("Subscribing to Bitget Futures kline updates for {Symbol} {Interval}",
            symbol, intervalBitget);

        var result = await _socketClient.FuturesApiV2.SubscribeToKlineUpdatesAsync(
            BitgetProductTypeV2.UsdtFutures,
            symbol,
            intervalBitget,
            data =>
            {
                try
                {
                    foreach (var kline in data.Data)
                    {
                        var candle = new Candle(
                            OpenTime: kline.OpenTime,
                            Open: kline.OpenPrice,
                            High: kline.HighPrice,
                            Low: kline.LowPrice,
                            Close: kline.ClosePrice,
                            Volume: kline.Volume,
                            CloseTime: kline.OpenTime.AddSeconds(GetIntervalSeconds(interval))
                        );

                        _logger.Debug("Bitget kline update: {Symbol} {Time} O:{Open} H:{High} L:{Low} C:{Close}",
                            symbol, kline.OpenTime, kline.OpenPrice, kline.HighPrice, kline.LowPrice, kline.ClosePrice);

                        onKlineUpdate(candle);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error processing Bitget kline update for {Symbol}", symbol);
                }
            },
            ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to subscribe to Bitget kline updates for {Symbol}: {Error}",
                symbol, result.Error?.Message);
            return null;
        }

        _klineSubscription = result.Data;
        _logger.Information("Successfully subscribed to Bitget kline updates for {Symbol}", symbol);

        return new SubscriptionWrapper(result.Data, () =>
        {
            _klineSubscription = null;
            _logger.Information("Unsubscribed from Bitget kline updates for {Symbol}", symbol);
        });
    }

    public async Task UnsubscribeAllAsync(CancellationToken ct = default)
    {
        _logger.Information("Unsubscribing from all Bitget kline streams");

        if (_klineSubscription != null)
        {
            await _klineSubscription.CloseAsync();
            _klineSubscription = null;
        }
    }

    private static int GetIntervalSeconds(CoreKlineInterval interval)
    {
        return interval switch
        {
            CoreKlineInterval.OneMinute => 60,
            CoreKlineInterval.FiveMinutes => 300,
            CoreKlineInterval.FifteenMinutes => 900,
            CoreKlineInterval.ThirtyMinutes => 1800,
            CoreKlineInterval.OneHour => 3600,
            CoreKlineInterval.FourHour => 14400,
            CoreKlineInterval.OneDay => 86400,
            CoreKlineInterval.OneWeek => 604800,
            _ => 60
        };
    }

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
            _subscription?.CloseAsync().GetAwaiter().GetResult();
            _onDispose();
        }
    }
}
