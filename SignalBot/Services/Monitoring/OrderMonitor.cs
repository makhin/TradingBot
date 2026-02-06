using SignalBot.Models;
using SignalBot.Services;
using SignalBot.State;
using TradingBot.Core.Exchanges;
using TradingBot.Core.Models;
using Serilog;
using System.Collections.Concurrent;

namespace SignalBot.Services.Monitoring;

/// <summary>
/// Monitors order execution via exchange User Data Stream
/// </summary>
public class OrderMonitor : ServiceBase, IOrderMonitor
{
    private readonly IExchangeOrderUpdateListener _updateListener;
    private readonly IPositionStore<SignalPosition> _store;
    private IDisposable? _subscription;
    private readonly ConcurrentDictionary<long, byte> _processedFilledOrders = new();

    public event Action<Guid, int, decimal>? OnTargetHit;
    public event Action<Guid, decimal>? OnStopLossHit;

    public bool IsMonitoring => IsRunning;

    public OrderMonitor(
        IExchangeOrderUpdateListener updateListener,
        IPositionStore<SignalPosition> store,
        ILogger? logger = null)
        : base(logger)
    {
        _updateListener = updateListener;
        _store = store;
    }

    protected override async Task OnStartAsync(CancellationToken ct)
    {
        _subscription = await _updateListener.SubscribeToOrderUpdatesAsync(HandleOrderUpdate, ct);

        if (_subscription == null)
        {
            throw new InvalidOperationException("Failed to subscribe to order updates");
        }
    }

    protected override async Task OnStopAsync(CancellationToken ct)
    {
        if (_subscription != null)
        {
            _subscription.Dispose();
            _subscription = null;
        }

        await _updateListener.UnsubscribeAllAsync();
    }

    private async void HandleOrderUpdate(OrderUpdate update)
    {
        try
        {
            // Only process filled orders
            if (update.Status != "Filled")
            {
                _logger.Debug("Order {OrderId} status: {Status}", update.OrderId, update.Status);
                return;
            }

            if (!_processedFilledOrders.TryAdd(update.OrderId, 0))
            {
                _logger.Debug("Duplicate filled update ignored for order {OrderId}", update.OrderId);
                return;
            }

            _logger.Information("Order filled: {Symbol} {OrderId} @ {Price}, Qty: {Qty}",
                update.Symbol, update.OrderId, update.AveragePrice, update.QuantityFilled);

            // Find position by symbol
            var position = await _store.GetPositionBySymbolAsync(update.Symbol);
            if (position == null)
            {
                _logger.Debug("No open position found for {Symbol}, ignoring order update", update.Symbol);
                return;
            }

            // Check if this is a stop loss order
            if (position.StopLossOrderId == update.OrderId)
            {
                var fillPrice = update.AveragePrice > 0 ? update.AveragePrice : update.Price;
                if (fillPrice <= 0 && position.CurrentStopLoss > 0)
                {
                    _logger.Warning(
                        "Stop loss fill price missing for {Symbol}; using current stop loss {StopLoss}",
                        update.Symbol, position.CurrentStopLoss);
                    fillPrice = position.CurrentStopLoss;
                }
                _logger.Information("Stop loss hit for {Symbol} @ {Price}", update.Symbol, fillPrice);
                OnStopLossHit?.Invoke(position.Id, fillPrice);
                return;
            }

            // Check if this is a take profit order
            for (int i = 0; i < position.TakeProfitOrderIds.Count; i++)
            {
                if (position.TakeProfitOrderIds[i] == update.OrderId)
                {
                    var targetIndex = i; // TP orders are in same order as targets
                    if (targetIndex < position.Targets.Count)
                    {
                        var fillPrice = update.AveragePrice > 0 ? update.AveragePrice : update.Price;
                        _logger.Information("Target {Index} hit for {Symbol} @ {Price}",
                            targetIndex + 1, update.Symbol, fillPrice);
                        OnTargetHit?.Invoke(position.Id, targetIndex, fillPrice);
                    }
                    return;
                }
            }

            _logger.Debug("Order {OrderId} not associated with SL or TP for position {PositionId}",
                update.OrderId, position.Id);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling order update for {Symbol} {OrderId}",
                update.Symbol, update.OrderId);
        }
    }
}
