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
/// Additionally performs periodic REST API position reconciliation as a safety net.
/// </summary>
public class OrderMonitor : ServiceBase, IOrderMonitor
{
    private readonly IExchangeOrderUpdateListener _updateListener;
    private readonly IFuturesExchangeClient _exchangeClient;
    private readonly IPositionStore<SignalPosition> _store;
    private IDisposable? _orderSubscription;
    private IDisposable? _positionSubscription;
    private CancellationTokenSource? _reconciliationCts;
    private Task? _reconciliationTask;
    private readonly ConcurrentDictionary<long, byte> _processedFilledOrders = new();
    private readonly ConcurrentDictionary<Guid, byte> _processedPositionClosures = new();

    private static readonly TimeSpan ReconciliationInterval = TimeSpan.FromSeconds(30);
    private const decimal PriceProximityThreshold = 0.005m; // 0.5%

    public event Action<Guid, int, decimal>? OnTargetHit;
    public event Action<Guid, decimal>? OnStopLossHit;
    public event Action<Guid, decimal, PositionCloseReason>? OnPositionClosedExternally;

    public bool IsMonitoring => IsRunning;

    public OrderMonitor(
        IExchangeOrderUpdateListener updateListener,
        IFuturesExchangeClient exchangeClient,
        IPositionStore<SignalPosition> store,
        ILogger? logger = null)
        : base(logger)
    {
        _updateListener = updateListener;
        _exchangeClient = exchangeClient;
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

        // Start periodic position reconciliation as additional safety net
        _reconciliationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _reconciliationTask = RunReconciliationLoopAsync(_reconciliationCts.Token);
        _logger.Information("Position reconciliation polling started (interval: {Interval}s)",
            ReconciliationInterval.TotalSeconds);
    }

    protected override async Task OnStopAsync(CancellationToken ct)
    {
        // Stop reconciliation loop
        if (_reconciliationCts != null)
        {
            _reconciliationCts.Cancel();
            if (_reconciliationTask != null)
            {
                try { await _reconciliationTask; }
                catch (OperationCanceledException) { }
            }
            _reconciliationCts.Dispose();
            _reconciliationCts = null;
            _reconciliationTask = null;
        }

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

            // Order ID doesn't match any stored SL/TP order.
            // This happens when Bitget triggers a SL/TP plan order — the resulting
            // market order has a NEW order ID different from the trigger order ID.
            // Detect these by checking if the order is closing the position.
            if (IsClosingOrderForPosition(position, update))
            {
                var fillPrice = update.AveragePrice > 0 ? update.AveragePrice : update.Price;
                HandleUnmatchedClosingOrder(position, update, fillPrice);
                return;
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

    /// <summary>
    /// Checks if a filled order is closing the position (opposite direction).
    /// </summary>
    private static bool IsClosingOrderForPosition(SignalPosition position, OrderUpdate update)
    {
        // A closing order has the opposite direction to the position
        var positionIsLong = position.Direction == SignalDirection.Long;
        var orderIsLong = update.Direction == TradeDirection.Long;
        return positionIsLong != orderIsLong;
    }

    /// <summary>
    /// Handles a filled order that is closing the position but doesn't match any stored
    /// SL/TP order IDs. Determines whether it was a triggered SL, TP, or other closure.
    /// </summary>
    private void HandleUnmatchedClosingOrder(SignalPosition position, OrderUpdate update, decimal fillPrice)
    {
        _logger.Information(
            "Detected unmatched closing order for {Symbol}: OrderId={OrderId}, Price={Price}, Qty={Qty}. " +
            "Likely a triggered SL/TP plan order.",
            position.Symbol, update.OrderId, fillPrice, update.QuantityFilled);

        // Check if fill price is near stop loss
        if (position.CurrentStopLoss > 0 && IsNearPrice(fillPrice, position.CurrentStopLoss))
        {
            _logger.Information("Triggered stop loss detected for {Symbol} @ {Price} (order {OrderId})",
                position.Symbol, fillPrice, update.OrderId);
            MarkPositionClosed(position.Id);
            OnStopLossHit?.Invoke(position.Id, fillPrice);
            return;
        }

        // Check if fill price is near any unhit target
        var targetIndex = FindNearestUnhitTarget(position, fillPrice);
        if (targetIndex >= 0)
        {
            _logger.Information("Triggered target {Index} detected for {Symbol} @ {Price} (order {OrderId})",
                targetIndex + 1, position.Symbol, fillPrice, update.OrderId);
            var unhitTargets = position.Targets.Count(t => !t.IsHit);
            if (unhitTargets <= 1)
            {
                MarkPositionClosed(position.Id);
            }
            OnTargetHit?.Invoke(position.Id, targetIndex, fillPrice);
            return;
        }

        // Could not match to SL or TP — treat as external closure
        _logger.Warning(
            "Unmatched closing order for {Symbol} @ {Price} does not match SL ({StopLoss}) or any target. " +
            "Treating as external closure.",
            position.Symbol, fillPrice, position.CurrentStopLoss);
        MarkPositionClosed(position.Id);
        var closeReason = DetermineCloseReason(position, fillPrice);
        OnPositionClosedExternally?.Invoke(position.Id, fillPrice, closeReason);
    }

    /// <summary>
    /// Finds the closest unhit target to the fill price (within proximity threshold).
    /// Returns the target index, or -1 if no match.
    /// </summary>
    private static int FindNearestUnhitTarget(SignalPosition position, decimal fillPrice)
    {
        int bestIndex = -1;
        decimal bestDistance = decimal.MaxValue;

        for (int i = 0; i < position.Targets.Count; i++)
        {
            var target = position.Targets[i];
            if (target.IsHit) continue;

            var distance = Math.Abs(fillPrice - target.Price) / target.Price;
            if (distance < PriceProximityThreshold && distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    /// <summary>
    /// Checks if two prices are within the proximity threshold.
    /// </summary>
    private static bool IsNearPrice(decimal price1, decimal price2)
    {
        if (price2 == 0) return false;
        return Math.Abs(price1 - price2) / price2 < PriceProximityThreshold;
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

    /// <summary>
    /// Periodically queries exchange REST API to detect positions that were closed
    /// without any WebSocket notification being received.
    /// </summary>
    private async Task RunReconciliationLoopAsync(CancellationToken ct)
    {
        // Initial delay to let WebSocket connections stabilize
        await Task.Delay(TimeSpan.FromSeconds(10), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ReconcilePositionsAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Position reconciliation check failed, will retry next cycle");
            }

            await Task.Delay(ReconciliationInterval, ct);
        }
    }

    private async Task ReconcilePositionsAsync(CancellationToken ct)
    {
        var trackedPositions = await _store.GetOpenPositionsAsync(ct);
        if (trackedPositions.Count == 0)
        {
            return;
        }

        List<FuturesPosition> exchangePositions;
        try
        {
            exchangePositions = await _exchangeClient.GetAllPositionsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to query exchange positions for reconciliation");
            return;
        }

        var exchangeSymbols = new HashSet<string>(
            exchangePositions
                .Where(p => p.Quantity > 0)
                .Select(p => p.Symbol),
            StringComparer.OrdinalIgnoreCase);

        foreach (var tracked in trackedPositions)
        {
            if (exchangeSymbols.Contains(tracked.Symbol))
            {
                continue; // Position still open on exchange
            }

            // Position is tracked locally but gone from exchange — it was closed
            if (!_processedPositionClosures.TryAdd(tracked.Id, 0))
            {
                continue; // Already being processed
            }

            _logger.Warning(
                "Position reconciliation: {Symbol} (id={PositionId}) is tracked locally but NOT found on exchange. " +
                "Detected as externally closed.",
                tracked.Symbol, tracked.Id);

            // We don't have the exact exit price from reconciliation,
            // so estimate it from mark price or SL/target levels
            var exitPrice = await EstimateExitPriceAsync(tracked, ct);
            var closeReason = DetermineCloseReason(tracked, exitPrice);

            _logger.Information(
                "Reconciliation closure for {Symbol}: estimated exit {ExitPrice}, reason {Reason}",
                tracked.Symbol, exitPrice, closeReason);

            OnPositionClosedExternally?.Invoke(tracked.Id, exitPrice, closeReason);
        }
    }

    private async Task<decimal> EstimateExitPriceAsync(SignalPosition position, CancellationToken ct)
    {
        // Try to get current mark price as best estimate
        try
        {
            var markPrice = await _exchangeClient.GetMarkPriceAsync(position.Symbol, ct);
            if (markPrice > 0)
            {
                return markPrice;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to get mark price for {Symbol} during reconciliation", position.Symbol);
        }

        // Fallback: use SL or entry price
        if (position.CurrentStopLoss > 0) return position.CurrentStopLoss;
        return position.ActualEntryPrice;
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
        if (position.CurrentStopLoss > 0 && IsNearPrice(exitPrice, position.CurrentStopLoss))
        {
            return PositionCloseReason.StopLossHit;
        }

        // Check if exit price is near any target
        foreach (var target in position.Targets.Where(t => !t.IsHit))
        {
            if (IsNearPrice(exitPrice, target.Price))
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
