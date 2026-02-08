using Bitget.Net.Clients;
using Bitget.Net.Enums;
using Bitget.Net.Enums.V2;
using CryptoExchange.Net.Sockets;
using CryptoExchange.Net.Objects.Sockets;
using BitgetOrderUpdate = TradingBot.Bitget.Futures.Models.OrderUpdate;
using BitgetPositionUpdate = TradingBot.Bitget.Futures.Models.PositionUpdate;
using TradingBot.Core.Models;
using Serilog;
using CorePositionSide = TradingBot.Core.Models.PositionSide;

namespace TradingBot.Bitget.Futures;

/// <summary>
/// Bitget Futures User Data Stream listener implementation using JK.Bitget.Net v3.4.0
/// </summary>
public class BitgetOrderUpdateListener
{
    private readonly BitgetSocketClient _socketClient;
    private readonly ILogger _logger;
    private UpdateSubscription? _orderSubscription;
    private UpdateSubscription? _positionSubscription;

    public bool IsSubscribed => _orderSubscription != null || _positionSubscription != null;

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

        var result = await _socketClient.FuturesApiV2.SubscribeToOrderUpdatesAsync(
            BitgetProductTypeV2.UsdtFutures,
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

                        _logger.Debug("Bitget order update: {Symbol} {OrderId} {Status}",
                            update.Symbol, update.OrderId, update.Status);

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
            _logger.Error("Failed to subscribe to Bitget order updates: {Error}", result.Error?.Message);

            // Demo credentials on the live websocket host trigger environment mismatch errors.
            // Return dummy subscription to allow the bot to continue without websocket monitoring.
            if (result.Error?.Message?.Contains("environment") == true ||
                result.Error?.Message?.Contains("Current environment does not match") == true)
            {
                _logger.Warning("⚠️ Bitget demo private websocket login failed due to environment mismatch");
                _logger.Warning("⚠️ Order monitoring via WebSocket is DISABLED for Bitget");
                _logger.Warning("⚠️ Bot will continue using REST API only");
                _logger.Information("Note: Configure BitgetSocketClient demo endpoint as wss://wspap.bitget.com with environment name 'demo'");

                // Return a dummy subscription that does nothing
                return new DummySubscription(_logger);
            }

            return null;
        }

        _orderSubscription = result.Data;
        _logger.Information("Successfully subscribed to Bitget order updates");

        return new SubscriptionWrapper(result.Data, () =>
        {
            _orderSubscription = null;
            _logger.Information("Unsubscribed from Bitget order updates");
        });
    }

    public async Task<IDisposable?> SubscribeToPositionUpdatesAsync(
        Action<BitgetPositionUpdate> onPositionUpdate,
        CancellationToken ct = default)
    {
        _logger.Information("Subscribing to Bitget Futures position updates");

        var result = await _socketClient.FuturesApiV2.SubscribeToPositionUpdatesAsync(
            BitgetProductTypeV2.UsdtFutures,
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

                        _logger.Debug("Bitget position update: {Symbol} {Side} {Quantity}",
                            update.Symbol, update.Side, update.Quantity);

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
            _logger.Error("Failed to subscribe to Bitget position updates: {Error}", result.Error?.Message);

            // Same handling as order updates: keep bot alive when websocket env is misconfigured.
            if (result.Error?.Message?.Contains("environment") == true ||
                result.Error?.Message?.Contains("Current environment does not match") == true)
            {
                _logger.Warning("⚠️ Position monitoring via WebSocket is DISABLED due to Bitget environment mismatch");
                return new DummySubscription(_logger);
            }

            return null;
        }

        _positionSubscription = result.Data;
        _logger.Information("Successfully subscribed to Bitget position updates");

        return new SubscriptionWrapper(result.Data, () =>
        {
            _positionSubscription = null;
            _logger.Information("Unsubscribed from Bitget position updates");
        });
    }

    public async Task UnsubscribeAllAsync(CancellationToken ct = default)
    {
        _logger.Information("Unsubscribing from all Bitget streams");

        if (_orderSubscription != null)
        {
            await _orderSubscription.CloseAsync();
            _orderSubscription = null;
        }

        if (_positionSubscription != null)
        {
            await _positionSubscription.CloseAsync();
            _positionSubscription = null;
        }
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
