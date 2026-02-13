using SignalBot.Models;
using SignalBot.State;
using SignalBot.Services.Statistics;
using TradingBot.Core.Exchanges;
using TradingBot.Core.Models;
using TradingBot.Core.Notifications;
using Serilog;
using Microsoft.Extensions.Options;
using SignalBot.Configuration;

namespace SignalBot.Services.Trading;

/// <summary>
/// Manages signal positions lifecycle (targets, stop-loss movement, P&L)
/// </summary>
public class PositionManager : IPositionManager
{
    private readonly IPositionStore<SignalPosition> _store;
    private readonly IFuturesOrderExecutor _orderExecutor;
    private readonly ITradeStatisticsService _tradeStatistics;
    private readonly INotifier? _notifier;
    private readonly string _quoteCurrency;
    private readonly ILogger _logger;

    public PositionManager(
        IPositionStore<SignalPosition> store,
        IFuturesOrderExecutor orderExecutor,
        ITradeStatisticsService tradeStatistics,
        IOptions<SignalBotSettings> settings,
        INotifier? notifier = null,
        ILogger? logger = null)
    {
        _store = store;
        _orderExecutor = orderExecutor;
        _tradeStatistics = tradeStatistics;
        _notifier = notifier;
        _quoteCurrency = string.IsNullOrWhiteSpace(settings.Value.Trading.DefaultSymbolSuffix)
            ? "USDT"
            : settings.Value.Trading.DefaultSymbolSuffix.Trim().ToUpperInvariant();
        _logger = logger ?? Log.ForContext<PositionManager>();
    }

    public async Task SavePositionAsync(SignalPosition position, CancellationToken ct = default)
    {
        await _store.SavePositionAsync(position, ct);
    }

    public async Task<SignalPosition?> GetPositionAsync(Guid positionId, CancellationToken ct = default)
    {
        return await _store.GetPositionAsync(positionId, ct);
    }

    public async Task<SignalPosition?> GetPositionBySymbolAsync(string symbol, CancellationToken ct = default)
    {
        return await _store.GetPositionBySymbolAsync(symbol, ct);
    }

    public async Task<List<SignalPosition>> GetOpenPositionsAsync(CancellationToken ct = default)
    {
        return await _store.GetOpenPositionsAsync(ct);
    }

    public async Task UpdatePositionAsync(SignalPosition position, CancellationToken ct = default)
    {
        await _store.SavePositionAsync(position, ct);
    }

    public async Task HandleTargetHitAsync(
        SignalPosition position,
        int targetIndex,
        decimal fillPrice,
        CancellationToken ct = default)
    {
        if (targetIndex < 0 || targetIndex >= position.Targets.Count)
        {
            _logger.Warning("Target index {Index} out of range for {Symbol}", targetIndex, position.Symbol);
            return;
        }

        var target = position.Targets[targetIndex];
        if (target.IsHit)
        {
            _logger.Information("Target {Index} already processed for {Symbol}, skipping duplicate",
                targetIndex + 1, position.Symbol);
            return;
        }

        if (fillPrice <= 0)
        {
            _logger.Warning(
                "Target {Index} fill price was {Price} for {Symbol}, using target price {TargetPrice} instead",
                targetIndex + 1, fillPrice, position.Symbol, target.Price);
            fillPrice = target.Price;
        }

        _logger.Information("Handling target {Index} hit for {Symbol} @ {Price}",
            targetIndex, position.Symbol, fillPrice);

        // 1. Update target as hit
        var updatedTargets = position.Targets.Select((t, i) => i == targetIndex
            ? t with
            {
                IsHit = true,
                HitAt = DateTime.UtcNow,
                ActualClosePrice = fillPrice
            }
            : t).ToList();

        // 2. Update remaining quantity
        decimal closedQty = target.QuantityToClose;
        decimal newRemaining = position.RemainingQuantity - closedQty;

        // 3. Calculate realized PnL for this portion
        decimal pnl = PnlCalculator.Calculate(
            position.ActualEntryPrice,
            fillPrice,
            closedQty,
            position.Direction);

        // 4. Move Stop Loss if needed
        if (target.MoveStopLossTo.HasValue && newRemaining > 0)
        {
            await MoveStopLossAsync(position, target.MoveStopLossTo.Value, newRemaining, ct);
        }

        // 5. Update position
        var updatedPosition = position with
        {
            Targets = updatedTargets,
            RemainingQuantity = newRemaining,
            RealizedPnl = position.RealizedPnl + pnl,
            Status = newRemaining <= 0 ? PositionStatus.Closed : PositionStatus.PartialClosed,
            ClosedAt = newRemaining <= 0 ? DateTime.UtcNow : null,
            CloseReason = newRemaining <= 0 ? PositionCloseReason.AllTargetsHit : null
        };

        await _store.SavePositionAsync(updatedPosition, ct);
        if (updatedPosition.Status == PositionStatus.Closed)
        {
            await _tradeStatistics.RecordClosedPositionAsync(updatedPosition, ct);
        }

        // 6. Notify
        if (_notifier != null)
        {
            await _notifier.SendMessageAsync(
                $"🎯 Target {targetIndex + 1} hit!\n" +
                $"Symbol: {position.Symbol}\n" +
                $"Price: {fillPrice}\n" +
                $"PnL: {pnl:+0.00;-0.00} {_quoteCurrency}\n" +
                $"Remaining: {newRemaining}",
                ct);
        }

        _logger.Information(
            "Target {Index} hit for {Symbol}: {Price}, PnL: {Pnl}, Remaining: {Remaining}",
            targetIndex + 1, position.Symbol, fillPrice, pnl, newRemaining);
    }

    public async Task HandleStopLossHitAsync(
        SignalPosition position,
        decimal fillPrice,
        CancellationToken ct = default)
    {
        _logger.Information("Handling stop loss hit for {Symbol} @ {Price}",
            position.Symbol, fillPrice);

        // 1. Cancel all TP orders
        foreach (var orderId in position.TakeProfitOrderIds)
        {
            try
            {
                await _orderExecutor.CancelOrderAsync(position.Symbol, orderId, ct);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to cancel TP order {OrderId} for {Symbol}",
                    orderId, position.Symbol);
            }
        }

        // 2. Calculate PnL
        decimal pnl = PnlCalculator.Calculate(
            position.ActualEntryPrice,
            fillPrice,
            position.RemainingQuantity,
            position.Direction);

        // 3. Update position
        var updatedPosition = position with
        {
            RemainingQuantity = 0,
            RealizedPnl = position.RealizedPnl + pnl,
            Status = PositionStatus.Closed,
            ClosedAt = DateTime.UtcNow,
            CloseReason = PositionCloseReason.StopLossHit
        };

        await _store.SavePositionAsync(updatedPosition, ct);
        await _tradeStatistics.RecordClosedPositionAsync(updatedPosition, ct);

        // 4. Notify
        if (_notifier != null)
        {
            await _notifier.SendMessageAsync(
                "🛑 Stop Loss Hit\n" +
                $"Symbol: {position.Symbol}\n" +
                $"Entry: {position.ActualEntryPrice}\n" +
                $"Exit: {fillPrice}\n" +
                $"Total PnL: {updatedPosition.RealizedPnl:+0.00;-0.00} {_quoteCurrency}",
                ct);
        }

        _logger.Information(
            "Stop loss hit for {Symbol}: Entry {Entry}, Exit {Exit}, PnL: {Pnl}",
            position.Symbol, position.ActualEntryPrice, fillPrice, updatedPosition.RealizedPnl);
    }

    public async Task HandlePositionClosedExternallyAsync(
        SignalPosition position,
        decimal exitPrice,
        PositionCloseReason closeReason,
        CancellationToken ct = default)
    {
        _logger.Information("Handling externally closed position for {Symbol} @ {Price}, reason: {Reason}",
            position.Symbol, exitPrice, closeReason);

        // Cancel any remaining TP orders (they may already be gone on the exchange)
        foreach (var orderId in position.TakeProfitOrderIds)
        {
            try
            {
                await _orderExecutor.CancelOrderAsync(position.Symbol, orderId, ct);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to cancel TP order {OrderId} for {Symbol} (may already be closed)",
                    orderId, position.Symbol);
            }
        }

        // Cancel SL order if exists
        if (position.StopLossOrderId.HasValue)
        {
            try
            {
                await _orderExecutor.CancelOrderAsync(position.Symbol, position.StopLossOrderId.Value, ct);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to cancel SL order {OrderId} for {Symbol} (may already be closed)",
                    position.StopLossOrderId.Value, position.Symbol);
            }
        }

        // Calculate PnL
        decimal pnl = PnlCalculator.Calculate(
            position.ActualEntryPrice,
            exitPrice,
            position.RemainingQuantity,
            position.Direction);

        // Update position
        var updatedPosition = position with
        {
            RemainingQuantity = 0,
            RealizedPnl = position.RealizedPnl + pnl,
            Status = PositionStatus.Closed,
            ClosedAt = DateTime.UtcNow,
            CloseReason = closeReason
        };

        await _store.SavePositionAsync(updatedPosition, ct);
        await _tradeStatistics.RecordClosedPositionAsync(updatedPosition, ct);

        // Notify
        if (_notifier != null)
        {
            var reasonEmoji = closeReason switch
            {
                PositionCloseReason.StopLossHit => "\ud83d\uded1",
                PositionCloseReason.AllTargetsHit => "\ud83c\udfaf",
                PositionCloseReason.Liquidation => "\ud83d\udca5",
                _ => "\u2139\ufe0f"
            };

            await _notifier.SendMessageAsync(
                $"{reasonEmoji} Position closed (exchange)\n" +
                $"Symbol: {position.Symbol}\n" +
                $"Reason: {closeReason}\n" +
                $"Entry: {position.ActualEntryPrice}\n" +
                $"Exit: {exitPrice}\n" +
                $"Total PnL: {updatedPosition.RealizedPnl:+0.00;-0.00} {_quoteCurrency}",
                ct);
        }

        _logger.Information(
            "Externally closed position for {Symbol}: Entry {Entry}, Exit {Exit}, PnL: {Pnl}, Reason: {Reason}",
            position.Symbol, position.ActualEntryPrice, exitPrice, updatedPosition.RealizedPnl, closeReason);
    }

    private async Task MoveStopLossAsync(
        SignalPosition position,
        decimal newStopLoss,
        decimal quantity,
        CancellationToken ct)
    {
        _logger.Information("Moving stop loss for {Symbol}: {OldSL} → {NewSL}",
            position.Symbol, position.CurrentStopLoss, newStopLoss);

        // 1. Cancel old SL
        if (position.StopLossOrderId.HasValue)
        {
            try
            {
                await _orderExecutor.CancelOrderAsync(position.Symbol, position.StopLossOrderId.Value, ct);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to cancel old stop loss for {Symbol}", position.Symbol);
            }
        }

        // 2. Place new SL
        try
        {
            var tradeDirection = position.Direction == SignalDirection.Long
                ? TradeDirection.Long
                : TradeDirection.Short;

            var result = await _orderExecutor.PlaceStopLossAsync(
                position.Symbol,
                tradeDirection,
                quantity,
                newStopLoss,
                ct);

            if (result.Success && result.OrderId != 0)
            {
                // Update position with new SL order ID
                var updatedPosition = position with
                {
                    StopLossOrderId = result.OrderId,
                    CurrentStopLoss = newStopLoss
                };
                await _store.SavePositionAsync(updatedPosition, ct);

                _logger.Information("Stop loss moved successfully for {Symbol}", position.Symbol);
            }
            else
            {
                _logger.Warning("Failed to place new stop loss for {Symbol}: {Reason}",
                    position.Symbol, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error moving stop loss for {Symbol}", position.Symbol);
        }
    }

}
