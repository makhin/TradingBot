using SignalBot.Models;
using SignalBot.Services;
using SignalBot.State;
using TradingBot.Core.Exchanges;
using TradingBot.Core.Models;
using Serilog;
using System.Collections.Concurrent;

namespace SignalBot.Services.Monitoring;

/// <summary>
/// Monitors order execution via exchange User Data Stream.
/// Also subscribes to position updates as a fallback to detect closures
/// not captured by order ID matching (e.g. exchange-managed TP/SL, liquidation).
/// </summary>
public class OrderMonitor : ServiceBase, IOrderMonitor
{
    private readonly IExchangeOrderUpdateListener _updateListener;
    private readonly IPositionStore<SignalPosition> _store;
    private IDisposable? _orderSubscription;
    private IDisposable? _positionSubscription;
    private readonly ConcurrentDictionary<long, byte> _processedFilledOrders = new();
    private readonly ConcurrentDictionary<Guid, byte> _processedPositionClosures = new();

    public event Action<Guid, int, decimal>? OnTargetHit;
    public event Action<Guid, decimal>? OnStopLossHit;
    public event Action<Guid, decimal, PositionCloseReason>? OnPositionClosedExternally;

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
        _orderSubscription = await _updateListener.SubscribeToOrderUpdatesAsync(HandleOrderUpdate, ct);

        if (_orderSubscription == null)
        {
            throw new InvalidOperationException("Failed to subscribe to order updates");
        }

        // Subscribe to position updates as fallback for detecting closures
        try
        {
            _positionSubscription = await _updateListener.SubscribeToPositionUpdatesAsync(HandlePositionUpdate, ct);
            if (_positionSubscription != null)
            {
                _logger.Information("Position update monitoring enabled (fallback closure detection)");
            }
            else
            {
                _logger.Warning("Position update subscription not available; relying on order matching only");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to subscribe to position updates; relying on order matching only");
        }
    }

    protected override async Task OnStopAsync(CancellationToken ct)
    {
        if (_orderSubscription != null)
        {
            _orderSubscription.Dispose();
            _orderSubscription = null;
        }

        if (_positionSubscription != null)
        {
            _positionSubscription.Dispose();
            _positionSubscription = null;
        }

        await _updateListener.UnsubscribeAllAsync();
    }

    /// <summary>
    /// Marks a position as already processed so the position update fallback won't fire for it.
    /// Called by order-matched handlers (SL/TP) to prevent duplicate processing.
    /// </summary>
    private void MarkPositionClosed(Guid positionId)
    {
        _processedPositionClosures.TryAdd(positionId, 0);
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
                MarkPositionClosed(position.Id);
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
                        // If this is the last target, mark position as closed to prevent duplicate from position update
                        var unhitTargets = position.Targets.Count(t => !t.IsHit);
                        if (unhitTargets <= 1)
                        {
                            MarkPositionClosed(position.Id);
                        }
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

    private async void HandlePositionUpdate(PositionUpdate update)
    {
        try
        {
            // Only interested in positions that went to zero (fully closed)
            if (update.PositionAmount != 0)
            {
                return;
            }

            // Find tracked position for this symbol
            var position = await _store.GetPositionBySymbolAsync(update.Symbol);
            if (position == null)
            {
                return;
            }

            // Already processed via order matching or a previous position update
            if (!_processedPositionClosures.TryAdd(position.Id, 0))
            {
                return;
            }

            // Small delay to allow order update handler to process first
            // (order updates and position updates may arrive near-simultaneously)
            await Task.Delay(2000);

            // Re-check position status after delay - it may have been closed by order handler
            var freshPosition = await _store.GetPositionAsync(position.Id);
            if (freshPosition == null || freshPosition.Status == PositionStatus.Closed)
            {
                _logger.Debug(
                    "Position {PositionId} for {Symbol} already closed by order handler, skipping fallback",
                    position.Id, position.Symbol);
                return;
            }

            // Position is still open in our store but closed on exchange - handle it
            var exitPrice = DetermineExitPrice(freshPosition, update);
            var closeReason = DetermineCloseReason(freshPosition, exitPrice);

            _logger.Warning(
                "Position {Symbol} closed externally (detected via position update). " +
                "Reason: {Reason}, Exit price: {ExitPrice}",
                freshPosition.Symbol, closeReason, exitPrice);

            OnPositionClosedExternally?.Invoke(freshPosition.Id, exitPrice, closeReason);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling position update for {Symbol}", update.Symbol);
        }
    }

    private static decimal DetermineExitPrice(SignalPosition position, PositionUpdate update)
    {
        // Use entry price from the update if available (mark price at close time)
        if (update.EntryPrice > 0)
        {
            return update.EntryPrice;
        }

        // Fallback to stop loss price
        if (position.CurrentStopLoss > 0)
        {
            return position.CurrentStopLoss;
        }

        return position.ActualEntryPrice;
    }

    private static PositionCloseReason DetermineCloseReason(SignalPosition position, decimal exitPrice)
    {
        // Try to figure out if this was SL, TP, or liquidation based on exit price
        var isLong = position.Direction == SignalDirection.Long;

        // Check if exit price is near stop loss
        if (position.CurrentStopLoss > 0)
        {
            var slDistance = Math.Abs(exitPrice - position.CurrentStopLoss) / position.CurrentStopLoss;
            if (slDistance < 0.005m) // within 0.5%
            {
                return PositionCloseReason.StopLossHit;
            }
        }

        // Check if exit price is near any target
        foreach (var target in position.Targets.Where(t => !t.IsHit))
        {
            var tpDistance = Math.Abs(exitPrice - target.Price) / target.Price;
            if (tpDistance < 0.005m) // within 0.5%
            {
                return PositionCloseReason.AllTargetsHit;
            }
        }

        // Check if it's a loss (price moved against direction) - likely SL or liquidation
        if (isLong && exitPrice < position.ActualEntryPrice)
        {
            return PositionCloseReason.StopLossHit;
        }
        if (!isLong && exitPrice > position.ActualEntryPrice)
        {
            return PositionCloseReason.StopLossHit;
        }

        // Price moved in favorable direction but doesn't match targets
        return PositionCloseReason.ManualClose;
    }
}
