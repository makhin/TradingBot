using System.Collections.Generic;
using Bitget.Net.Clients;
using Bitget.Net.Enums;
using Bitget.Net.Enums.V2;
using CryptoExchange.Net.Sockets;
using CryptoExchange.Net.Objects.Sockets;
using BitgetOrderUpdate = TradingBot.Bitget.Futures.Models.OrderUpdate;
using BitgetPositionUpdate = TradingBot.Bitget.Futures.Models.PositionUpdate;
using TradingBot.Core.Models;
using Serilog;
using TradingBot.Bitget.Common;
using CorePositionSide = TradingBot.Core.Models.PositionSide;

namespace TradingBot.Bitget.Futures;

/// <summary>
/// Bitget Futures User Data Stream listener implementation using JK.Bitget.Net v3.4.0
/// </summary>
public class BitgetOrderUpdateListener
{
    private readonly BitgetSocketClient _socketClient;
    private readonly ILogger _logger;
    private List<UpdateSubscription> _orderSubscriptions = new();
    private List<UpdateSubscription> _positionSubscriptions = new();

    public bool IsSubscribed => _orderSubscriptions.Count > 0 || _positionSubscriptions.Count > 0;

    public BitgetOrderUpdateListener(BitgetSocketClient socketClient, ILogger? logger = null)
    {
        _socketClient = socketClient;
        _logger = logger ?? Log.ForContext<BitgetOrderUpdateListener>();
    }

    public async Task<IDisposable?> SubscribeToOrderUpdatesAsync(
        Action<BitgetOrderUpdate> onOrderUpdate,
        CancellationToken ct = default)
    {
        _logger.Information("Subscribing to Bitget Futures order updates");
        var subscriptions = new List<UpdateSubscription>();
        var hadEnvironmentMismatch = false;

        foreach (var (productType, _) in BitgetHelpers.GetSupportedPerpetualMarkets())
        {
            var result = await _socketClient.FuturesApiV2.SubscribeToOrderUpdatesAsync(
                productType,
                data =>
                {
                    try
                    {
                        foreach (var order in data.Data)
                        {
                            var direction = order.Side == OrderSide.Buy ? TradeDirection.Long : TradeDirection.Short;

                            var update = new BitgetOrderUpdate
                            {
                                Symbol = order.Symbol,
                                OrderId = long.Parse(order.OrderId),
                                ClientOrderId = order.ClientOrderId ?? string.Empty,
                                Status = MapOrderStatus(order.Status.ToString()),
                                Side = direction,
                                Price = order.Price ?? 0m,
                                Quantity = order.Quantity,
                                FilledQuantity = order.QuantityFilled,
                                AveragePrice = order.AveragePrice ?? 0m,
                                UpdateTime = order.UpdateTime ?? DateTime.UtcNow
                            };

                            _logger.Debug("Bitget order update ({ProductType}): {Symbol} {OrderId} {Status}",
                                productType, update.Symbol, update.OrderId, update.Status);

                            onOrderUpdate(update);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error processing Bitget order update");
                    }
                },
                ct: ct);

            if (!result.Success)
            {
                _logger.Warning("Failed to subscribe to Bitget order updates for {ProductType}: {Error}",
                    productType, result.Error?.Message);

                if (result.Error?.Message?.Contains("environment") == true ||
                    result.Error?.Message?.Contains("Current environment does not match") == true)
                {
                    hadEnvironmentMismatch = true;
                }

                continue;
            }

            subscriptions.Add(result.Data);
            _logger.Information("Successfully subscribed to Bitget order updates for {ProductType}", productType);
        }

        if (subscriptions.Count == 0)
        {
            if (hadEnvironmentMismatch)
            {
                _logger.Warning("⚠️ Bitget demo private websocket login failed due to environment mismatch");
                _logger.Warning("⚠️ Order monitoring via WebSocket is DISABLED for Bitget");
                _logger.Warning("⚠️ Bot will continue using REST API only");
                _logger.Information("Note: Configure BitgetSocketClient demo endpoint as wss://wspap.bitget.com with environment name 'demo'");
                return new DummySubscription(_logger);
            }

            return null;
        }

        _orderSubscriptions = subscriptions;
        return new SubscriptionWrapper(subscriptions, () =>
        {
            _orderSubscriptions.Clear();
            _logger.Information("Unsubscribed from Bitget order updates");
        });
    }

    public async Task<IDisposable?> SubscribeToPositionUpdatesAsync(
        Action<BitgetPositionUpdate> onPositionUpdate,
        CancellationToken ct = default)
    {
        _logger.Information("Subscribing to Bitget Futures position updates");
        var subscriptions = new List<UpdateSubscription>();
        var hadEnvironmentMismatch = false;

        foreach (var (productType, _) in BitgetHelpers.GetSupportedPerpetualMarkets())
        {
            var result = await _socketClient.FuturesApiV2.SubscribeToPositionUpdatesAsync(
                productType,
                data =>
                {
                    try
                    {
                        foreach (var position in data.Data)
                        {
                            var side = position.Total > 0 ? CorePositionSide.Long : CorePositionSide.Short;

                            var update = new BitgetPositionUpdate
                            {
                                Symbol = position.Symbol,
                                Side = side,
                                Quantity = Math.Abs(position.Total),
                                EntryPrice = position.AverageOpenPrice,
                                MarkPrice = position.MarkPrice,
                                UnrealizedPnl = position.UnrealizedProfitAndLoss,
                                LiquidationPrice = position.LiquidationPrice,
                                UpdateTime = DateTime.UtcNow
                            };

                            _logger.Debug("Bitget position update ({ProductType}): {Symbol} {Side} {Quantity}",
                                productType, update.Symbol, update.Side, update.Quantity);

                            onPositionUpdate(update);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error processing Bitget position update");
                    }
                },
                ct: ct);

            if (!result.Success)
            {
                _logger.Warning("Failed to subscribe to Bitget position updates for {ProductType}: {Error}",
                    productType, result.Error?.Message);

                if (result.Error?.Message?.Contains("environment") == true ||
                    result.Error?.Message?.Contains("Current environment does not match") == true)
                {
                    hadEnvironmentMismatch = true;
                }

                continue;
            }

            subscriptions.Add(result.Data);
            _logger.Information("Successfully subscribed to Bitget position updates for {ProductType}", productType);
        }

        if (subscriptions.Count == 0)
        {
            if (hadEnvironmentMismatch)
            {
                _logger.Warning("⚠️ Position monitoring via WebSocket is DISABLED due to Bitget environment mismatch");
                return new DummySubscription(_logger);
            }

            return null;
        }

        _positionSubscriptions = subscriptions;
        return new SubscriptionWrapper(subscriptions, () =>
        {
            _positionSubscriptions.Clear();
            _logger.Information("Unsubscribed from Bitget position updates");
        });
    }

    public async Task UnsubscribeAllAsync(CancellationToken ct = default)
    {
        _logger.Information("Unsubscribing from all Bitget streams");

        foreach (var orderSubscription in _orderSubscriptions)
        {
            await orderSubscription.CloseAsync();
        }
        _orderSubscriptions.Clear();

        foreach (var positionSubscription in _positionSubscriptions)
        {
            await positionSubscription.CloseAsync();
        }
        _positionSubscriptions.Clear();
    }

    private static string MapOrderStatus(string status)
    {
        // Map Bitget order status to common status strings
        return status?.ToLower() switch
        {
            "filled" => "Filled",
            "partial_fill" => "PartiallyFilled",
            "cancelled" => "Canceled",
            "new" => "New",
            "live" => "New",
            _ => status ?? "Unknown"
        };
    }

    private class SubscriptionWrapper : IDisposable
    {
        private readonly IReadOnlyCollection<UpdateSubscription> _subscriptions;
        private readonly Action _onDispose;

        public SubscriptionWrapper(IReadOnlyCollection<UpdateSubscription> subscriptions, Action onDispose)
        {
            _subscriptions = subscriptions;
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.CloseAsync().GetAwaiter().GetResult();
            }

            _onDispose();
        }
    }

    /// <summary>
    /// Dummy subscription for when WebSocket is not available (e.g., demo trading)
    /// </summary>
    private class DummySubscription : IDisposable
    {
        private readonly ILogger _logger;

        public DummySubscription(ILogger logger)
        {
            _logger = logger;
        }

        public void Dispose()
        {
            _logger.Debug("Dummy subscription disposed (no-op)");
        }
    }
}
