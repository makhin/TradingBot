using Binance.Net.Clients;
using Binance.Net.Enums;
using CryptoExchange.Net.Sockets;
using CryptoExchange.Net.Objects.Sockets;
using TradingBot.Core.Models;
using TradingBot.Binance.Common.Interfaces;
using Serilog;

namespace TradingBot.Binance.Futures;

/// <summary>
/// Binance Futures User Data Stream listener implementation
/// </summary>
public class FuturesOrderUpdateListener : IOrderUpdateListener
{
    private readonly BinanceSocketClient _socketClient;
    private readonly BinanceRestClient _restClient;
    private readonly ILogger _logger;
    private UpdateSubscription? _currentSubscription;
    private string? _listenKey;

    public bool IsSubscribed => _currentSubscription != null;

    public FuturesOrderUpdateListener(
        BinanceSocketClient socketClient,
        BinanceRestClient restClient,
        ILogger? logger = null)
    {
        _socketClient = socketClient;
        _restClient = restClient;
        _logger = logger ?? Log.ForContext<FuturesOrderUpdateListener>();
    }

    /// <summary>
    /// Subscribes to order execution updates
    /// </summary>
    public async Task<IDisposable?> SubscribeToOrderUpdatesAsync(
        Action<OrderUpdate> onOrderUpdate,
        CancellationToken ct = default)
    {
        var listenKey = await GetListenKeyAsync(ct);
        if (listenKey == null)
        {
            _logger.Error("Failed to get listen key for User Data Stream");
            return null;
        }

        _logger.Information("Subscribing to Futures order updates");

        var result = await _socketClient.UsdFuturesApi.Account.SubscribeToUserDataUpdatesAsync(
            listenKey,
            onLeverageUpdate: null,
            onMarginUpdate: null,
            onAccountUpdate: null,
            onOrderUpdate: data =>
            {
                var order = data.Data.UpdateData;
                var direction = order.Side == OrderSide.Buy ? TradeDirection.Long : TradeDirection.Short;

                var update = new OrderUpdate
                {
                    Symbol = order.Symbol,
                    OrderId = order.OrderId,
                    Status = order.Status.ToString(),
                    Direction = direction,
                    Quantity = order.Quantity,
                    Price = order.Price,
                    AveragePrice = order.AveragePrice,
                    QuantityFilled = order.AccumulatedQuantityOfFilledTrades,
                    UpdateTime = order.UpdateTime,
                    OrderType = order.Type.ToString(),
                    TimeInForce = order.TimeInForce.ToString()
                };

                _logger.Debug("Order update: {Symbol} {OrderId} {Status}", update.Symbol, update.OrderId, update.Status);
                onOrderUpdate(update);
            },
            onTradeUpdate: null,
            onListenKeyExpired: null,
            onStrategyUpdate: null,
            onGridUpdate: null,
            onConditionOrderTriggerRejectUpdate: null,
            onAlgoOrderUpdate: data =>
            {
                var order = data.Data.Order;
                var direction = order.Side == OrderSide.Buy ? TradeDirection.Long : TradeDirection.Short;
                var status = order.Status is AlgoOrderStatus.Triggered or AlgoOrderStatus.Finished
                    ? "Filled"
                    : order.Status.ToString();

                var update = new OrderUpdate
                {
                    Symbol = order.Symbol,
                    OrderId = order.Id,
                    Status = status,
                    Direction = direction,
                    Quantity = order.Quantity,
                    Price = order.Price,
                    AveragePrice = order.AverageFillPrice > 0 ? order.AverageFillPrice : order.Price,
                    QuantityFilled = order.QuantityFilled,
                    UpdateTime = data.Data.Timestamp,
                    OrderType = order.Type.ToString(),
                    TimeInForce = order.TimeInForce?.ToString()
                };

                _logger.Debug(
                    "Algo order update: {Symbol} {OrderId} {Status}",
                    update.Symbol, update.OrderId, update.Status);
                onOrderUpdate(update);
            },
            ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to subscribe to order updates: {Error}", result.Error?.Message);
            return null;
        }

        _currentSubscription = result.Data;
        _logger.Information("Successfully subscribed to order updates");

        // Start keep-alive task
        _ = KeepListenKeyAliveAsync(listenKey, ct);

        return new SubscriptionWrapper(result.Data, () =>
        {
            _currentSubscription = null;
            _logger.Information("Unsubscribed from order updates");
        });
    }

    /// <summary>
    /// Subscribes to position updates
    /// </summary>
    public async Task<IDisposable?> SubscribeToPositionUpdatesAsync(
        Action<PositionUpdate> onPositionUpdate,
        CancellationToken ct = default)
    {
        var listenKey = await GetListenKeyAsync(ct);
        if (listenKey == null)
        {
            _logger.Error("Failed to get listen key for User Data Stream");
            return null;
        }

        _logger.Information("Subscribing to Futures position updates");

        var result = await _socketClient.UsdFuturesApi.Account.SubscribeToUserDataUpdatesAsync(
            listenKey,
            onLeverageUpdate: null,
            onMarginUpdate: null,
            onAccountUpdate: data =>
            {
                foreach (var position in data.Data.UpdateData.Positions)
                {
                    if (position.Quantity == 0)
                        continue;

                    var side = position.Quantity > 0 ? TradeDirection.Long : TradeDirection.Short;

                    var update = new PositionUpdate
                    {
                        Symbol = position.Symbol,
                        PositionAmount = Math.Abs(position.Quantity),
                        EntryPrice = position.EntryPrice,
                        UnrealizedPnl = position.UnrealizedPnl,
                        UpdateTime = data.Data.EventTime,
                        Side = side
                    };

                    _logger.Debug("Position update: {Symbol} {Amount} {PnL}", update.Symbol, update.PositionAmount, update.UnrealizedPnl);
                    onPositionUpdate(update);
                }
            },
            onOrderUpdate: null,
            onListenKeyExpired: null,
            ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to subscribe to position updates: {Error}", result.Error?.Message);
            return null;
        }

        _currentSubscription = result.Data;
        _logger.Information("Successfully subscribed to position updates");

        // Start keep-alive task
        _ = KeepListenKeyAliveAsync(listenKey, ct);

        return new SubscriptionWrapper(result.Data, () =>
        {
            _currentSubscription = null;
            _logger.Information("Unsubscribed from position updates");
        });
    }

    /// <summary>
    /// Subscribes to account balance updates
    /// </summary>
    public async Task<IDisposable?> SubscribeToAccountUpdatesAsync(
        Action<AccountUpdate> onAccountUpdate,
        CancellationToken ct = default)
    {
        var listenKey = await GetListenKeyAsync(ct);
        if (listenKey == null)
        {
            _logger.Error("Failed to get listen key for User Data Stream");
            return null;
        }

        _logger.Information("Subscribing to Futures account updates");

        var result = await _socketClient.UsdFuturesApi.Account.SubscribeToUserDataUpdatesAsync(
            listenKey,
            onLeverageUpdate: null,
            onMarginUpdate: null,
            onAccountUpdate: data =>
            {
                foreach (var balance in data.Data.UpdateData.Balances)
                {
                    var update = new AccountUpdate
                    {
                        Asset = balance.Asset,
                        Balance = balance.WalletBalance,
                        AvailableBalance = balance.CrossWalletBalance,
                        UpdateTime = data.Data.EventTime
                    };

                    _logger.Debug("Account update: {Asset} {Balance}", update.Asset, update.Balance);
                    onAccountUpdate(update);
                }
            },
            onOrderUpdate: null,
            onListenKeyExpired: null,
            ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to subscribe to account updates: {Error}", result.Error?.Message);
            return null;
        }

        _currentSubscription = result.Data;
        _logger.Information("Successfully subscribed to account updates");

        // Start keep-alive task
        _ = KeepListenKeyAliveAsync(listenKey, ct);

        return new SubscriptionWrapper(result.Data, () =>
        {
            _currentSubscription = null;
            _logger.Information("Unsubscribed from account updates");
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
            _logger.Information("Closed all Futures User Data Stream subscriptions");
        }

        if (_listenKey != null)
        {
            await CloseListenKeyAsync(_listenKey);
            _listenKey = null;
        }
    }

    /// <summary>
    /// Gets or creates a listen key for the User Data Stream
    /// </summary>
    private async Task<string?> GetListenKeyAsync(CancellationToken ct)
    {
        if (_listenKey != null)
            return _listenKey;

        var result = await _restClient.UsdFuturesApi.Account.StartUserStreamAsync(ct);
        if (!result.Success)
        {
            _logger.Error("Failed to start User Data Stream: {Error}", result.Error?.Message);
            return null;
        }

        _listenKey = result.Data;
        _logger.Information("Started User Data Stream with listen key");
        return _listenKey;
    }

    /// <summary>
    /// Keeps the listen key alive by pinging it every 30 minutes
    /// </summary>
    private async Task KeepListenKeyAliveAsync(string listenKey, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _currentSubscription != null)
            {
                await Task.Delay(TimeSpan.FromMinutes(30), ct);

                var result = await _restClient.UsdFuturesApi.Account.KeepAliveUserStreamAsync(listenKey, ct);
                if (!result.Success)
                {
                    _logger.Warning("Failed to keep listen key alive: {Error}", result.Error?.Message);
                }
                else
                {
                    _logger.Debug("Listen key kept alive");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Listen key keep-alive cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in listen key keep-alive task");
        }
    }

    /// <summary>
    /// Closes the listen key
    /// </summary>
    private async Task CloseListenKeyAsync(string listenKey)
    {
        try
        {
            var result = await _restClient.UsdFuturesApi.Account.StopUserStreamAsync(listenKey);
            if (!result.Success)
            {
                _logger.Warning("Failed to close listen key: {Error}", result.Error?.Message);
            }
            else
            {
                _logger.Information("Closed User Data Stream");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error closing listen key");
        }
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
