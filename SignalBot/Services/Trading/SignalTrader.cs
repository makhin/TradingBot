using System.Diagnostics;
using SignalBot.Configuration;
using SignalBot.Models;
using SignalBot.Telemetry;
using TradingBot.Binance.Futures.Interfaces;
using TradingBot.Binance.Futures.Models;
using TradingBot.Core.Models;
using TradingBot.Core.RiskManagement;
using Serilog;
using Polly;
using Serilog.Context;
using TradingBot.Binance.Common.Models;

namespace SignalBot.Services.Trading;

/// <summary>
/// Executes trading signals on Binance Futures
/// </summary>
public class SignalTrader : ISignalTrader
{
    private readonly IBinanceFuturesClient _client;
    private readonly IFuturesOrderExecutor _orderExecutor;
    private readonly IPositionManager _positionManager;
    private readonly IRiskManager _riskManager;
    private readonly TradingSettings _settings;
    private readonly EntrySettings _entrySettings;
    private readonly ILogger _logger;
    private readonly IAsyncPolicy<ExecutionResult> _retryPolicy;

    public SignalTrader(
        IBinanceFuturesClient client,
        IFuturesOrderExecutor orderExecutor,
        IPositionManager positionManager,
        IRiskManager riskManager,
        TradingSettings settings,
        EntrySettings entrySettings,
        IAsyncPolicy<ExecutionResult> retryPolicy,
        ILogger? logger = null)
    {
        _client = client;
        _orderExecutor = orderExecutor;
        _positionManager = positionManager;
        _riskManager = riskManager;
        _settings = settings;
        _entrySettings = entrySettings;
        _retryPolicy = retryPolicy;
        _logger = logger ?? Log.ForContext<SignalTrader>();
    }

    public async Task<SignalPosition> ExecuteSignalAsync(
        TradingSignal signal,
        decimal accountEquity,
        CancellationToken ct = default)
    {
        using var activity = SignalBotTelemetry.ActivitySource.StartActivity("Execute", ActivityKind.Internal);
        activity?.SetTag("signal.id", signal.Id);
        activity?.SetTag("signal.symbol", signal.Symbol);
        activity?.SetTag("signal.direction", signal.Direction.ToString());

        using var signalContext = LogContext.PushProperty("SignalId", signal.Id);

        // 1. Create position in Pending status
        var position = new SignalPosition
        {
            SignalId = signal.Id,
            Symbol = signal.Symbol,
            Direction = signal.Direction,
            Status = PositionStatus.Pending,
            PlannedEntryPrice = signal.Entry,
            CurrentStopLoss = signal.AdjustedStopLoss,
            Leverage = signal.AdjustedLeverage,
            Targets = CreateTargetLevels(signal, 0) // Temporary, will be recalculated after position size is known
        };

        await _positionManager.SavePositionAsync(position, ct);
        using var positionContext = LogContext.PushProperty("PositionId", position.Id);
        activity?.SetTag("position.id", position.Id);

        try
        {
            // 2. Set leverage and margin type
            var leverageSet = await _client.SetLeverageAsync(signal.Symbol, signal.AdjustedLeverage, ct);
            if (!leverageSet)
            {
                _logger.Warning("Failed to set leverage for {Symbol}, continuing anyway", signal.Symbol);
            }

            var marginTypeSet = await _client.SetMarginTypeAsync(signal.Symbol, MarginType.Isolated, ct);
            if (!marginTypeSet)
            {
                _logger.Warning("Failed to set margin type for {Symbol}, may already be set", signal.Symbol);
            }

            // 3. Check price deviation
            var currentPrice = await _client.GetMarkPriceAsync(signal.Symbol, ct);
            var deviationPercent = Math.Abs(currentPrice - signal.Entry) / signal.Entry * 100;

            if (deviationPercent > _entrySettings.MaxPriceDeviationPercent)
            {
                _logger.Warning(
                    "Price deviation {Deviation:F2}% exceeds max {Max:F2}% for {Symbol} (Signal: {Entry}, Current: {Current})",
                    deviationPercent, _entrySettings.MaxPriceDeviationPercent, signal.Symbol, signal.Entry, currentPrice);

                switch (_entrySettings.DeviationAction)
                {
                    case PriceDeviationAction.Skip:
                        _logger.Information("Skipping signal for {Symbol} due to price deviation", signal.Symbol);
                        position = position with { Status = PositionStatus.Cancelled };
                        await _positionManager.SavePositionAsync(position, ct);
                        return position;

                    case PriceDeviationAction.EnterAtMarket:
                        _logger.Information("Entering at market despite deviation for {Symbol}", signal.Symbol);
                        // Continue with execution
                        break;

                    case PriceDeviationAction.PlaceLimitAtEntry:
                        _logger.Information("Placing limit order at entry price {Entry} for {Symbol}", signal.Entry, signal.Symbol);
                        // TODO: Implement limit order logic
                        throw new NotImplementedException("Limit order placement not yet implemented");

                    case PriceDeviationAction.EnterAndAdjustTargets:
                        _logger.Information("Entering at market and adjusting targets for {Symbol}", signal.Symbol);
                        signal = AdjustTargetsForPriceDeviation(signal, currentPrice);
                        break;
                }
            }
            else
            {
                _logger.Information(
                    "Price deviation {Deviation:F2}% is within acceptable range for {Symbol}",
                    deviationPercent, signal.Symbol);
            }

            // 4. Calculate position size
            var positionSize = _riskManager.CalculatePositionSize(
                signal.Entry,
                signal.AdjustedStopLoss);

            position = position with
            {
                InitialQuantity = positionSize.Quantity,
                RemainingQuantity = positionSize.Quantity,
                Status = PositionStatus.Opening,
                Targets = CreateTargetLevels(signal, positionSize.Quantity)
            };
            await _positionManager.SavePositionAsync(position, ct);

            // 4. Open position (Market order)
            var tradeDirection = signal.Direction == SignalDirection.Long
                ? TradeDirection.Long
                : TradeDirection.Short;

            var entryResult = await PlaceMarketOrderWithRetry(
                signal.Symbol,
                tradeDirection,
                positionSize.Quantity,
                ct);

            if (!entryResult.IsAcceptable || !entryResult.OrderId.HasValue)
            {
                position = position with { Status = PositionStatus.Failed };
                await _positionManager.SavePositionAsync(position, ct);
                throw new InvalidOperationException($"Entry order failed: {entryResult.RejectReason}");
            }

            position = position with
            {
                EntryOrderId = entryResult.OrderId.Value,
                ActualEntryPrice = entryResult.ActualPrice,
                OpenedAt = DateTime.UtcNow,
                Status = PositionStatus.Open
            };

            // 5. Place Stop Loss order
            var slResult = await PlaceStopLossOrderWithRetry(
                signal.Symbol,
                signal.Direction,
                positionSize.Quantity,
                signal.AdjustedStopLoss,
                ct);

            if (slResult.IsAcceptable && slResult.OrderId.HasValue)
            {
                position = position with { StopLossOrderId = slResult.OrderId.Value };
            }
            else
            {
                _logger.Error("Stop loss placement failed for {Symbol}: {Reason}. Closing position at market.",
                    signal.Symbol, slResult.RejectReason);

                var closeDirection = tradeDirection == TradeDirection.Long
                    ? TradeDirection.Short
                    : TradeDirection.Long;
                var closeResult = await _orderExecutor.PlaceMarketOrderAsync(
                    signal.Symbol,
                    closeDirection,
                    positionSize.Quantity,
                    ct);

                if (!closeResult.IsAcceptable)
                {
                    position = position with { Status = PositionStatus.Failed };
                    await _positionManager.SavePositionAsync(position, ct);
                    throw new InvalidOperationException(
                        $"Stop loss placement failed and market close failed: {closeResult.RejectReason}");
                }

                var realizedPnl = closeResult.ActualPrice > 0
                    ? PnlCalculator.Calculate(
                        position.ActualEntryPrice,
                        closeResult.ActualPrice,
                        positionSize.Quantity,
                        position.Direction)
                    : 0m;

                position = position with
                {
                    RemainingQuantity = 0,
                    RealizedPnl = position.RealizedPnl + realizedPnl,
                    Status = PositionStatus.Closed,
                    ClosedAt = DateTime.UtcNow,
                    CloseReason = PositionCloseReason.Error
                };

                await _positionManager.SavePositionAsync(position, ct);
                throw new InvalidOperationException(
                    $"Stop loss placement failed; position closed at market: {slResult.RejectReason}");
            }

            // 6. Place Take Profit orders for each target
            var tpOrderIds = new List<long>();
            foreach (var target in position.Targets)
            {
                var tpResult = await PlaceTakeProfitOrderWithRetry(
                    signal.Symbol,
                    signal.Direction,
                    target.QuantityToClose,
                    target.Price,
                    ct);

                if (tpResult.OrderId.HasValue)
                {
                    tpOrderIds.Add(tpResult.OrderId.Value);
                }
                else
                {
                    _logger.Warning("Failed to place take profit for target {Index} on {Symbol}: {Reason}",
                        target.Index, signal.Symbol, tpResult.RejectReason);
                }
            }

            position = position with { TakeProfitOrderIds = tpOrderIds };
            await _positionManager.SavePositionAsync(position, ct);

            _logger.Information(
                "Position opened: {Symbol} {Direction} @ {Price}, SL: {SL}, Qty: {Qty}, Leverage: {Leverage}x",
                position.Symbol, position.Direction, position.ActualEntryPrice,
                position.CurrentStopLoss, position.InitialQuantity, position.Leverage);

            return position;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.Error(ex, "Failed to execute signal {SignalId} for {Symbol}",
                signal.Id, signal.Symbol);

            position = position with { Status = PositionStatus.Failed };
            await _positionManager.SavePositionAsync(position, ct);

            throw;
        }
    }

    private IReadOnlyList<TargetLevel> CreateTargetLevels(TradingSignal signal, decimal totalQuantity)
    {
        var targets = new List<TargetLevel>();
        var percents = _settings.TargetClosePercents;

        for (int i = 0; i < signal.Targets.Count && i < percents.Count; i++)
        {
            var percentToClose = percents[i];
            var quantityToClose = totalQuantity * (percentToClose / 100m);
            targets.Add(new TargetLevel
            {
                Index = i,
                Price = signal.Targets[i],
                PercentToClose = percentToClose,
                QuantityToClose = quantityToClose,
                MoveStopLossTo = GetMoveStopLossTo(i, signal)
            });
        }

        return targets;
    }

    private decimal? GetMoveStopLossTo(int index, TradingSignal signal)
    {
        if (!_settings.MoveStopToBreakeven)
        {
            return null;
        }

        return index == 0
            ? signal.Entry  // After T1, move SL to breakeven
            : signal.Targets[index - 1];  // After T2, move to T1, etc.
    }

    private TradingSignal AdjustTargetsForPriceDeviation(TradingSignal signal, decimal actualEntry)
    {
        // Calculate the shift between planned and actual entry
        decimal shift = actualEntry - signal.Entry;

        _logger.Information(
            "Adjusting targets for {Symbol}: Entry shift from {Original} to {Actual} ({Shift:+0.0000;-0.0000})",
            signal.Symbol, signal.Entry, actualEntry, shift);

        // Shift all targets by the same amount
        var adjustedTargets = signal.Targets.Select(t => t + shift).ToList();

        _logger.Information(
            "Original targets: [{Original}], Adjusted targets: [{Adjusted}]",
            string.Join(", ", signal.Targets.Select(t => t.ToString("F4"))),
            string.Join(", ", adjustedTargets.Select(t => t.ToString("F4"))));

        return signal with
        {
            Entry = actualEntry,
            Targets = adjustedTargets
        };
    }

    private async Task<ExecutionResult> PlaceMarketOrderWithRetry(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        CancellationToken ct)
    {
        return await ExecuteWithRetryAsync(
            ct,
            token => _orderExecutor.PlaceMarketOrderAsync(symbol, direction, quantity, token),
            (attempt, max) => _logger.Information(
                "Placing market {Direction} order for {Symbol}, attempt {Attempt}/{Max}",
                direction, symbol, attempt, max),
            (attempt, ex) => _logger.Warning(ex, "Market order attempt {Attempt} failed for {Symbol}", attempt, symbol),
            "Market order placement failed",
            throwOnFailure: true);
    }

    private async Task<ExecutionResult> PlaceStopLossOrderWithRetry(
        string symbol,
        SignalDirection direction,
        decimal quantity,
        decimal stopPrice,
        CancellationToken ct)
    {
        var tradeDirection = direction == SignalDirection.Long
            ? TradeDirection.Long
            : TradeDirection.Short;

        return await ExecuteWithRetryAsync(
            ct,
            token => _orderExecutor.PlaceStopLossAsync(symbol, tradeDirection, quantity, stopPrice, token),
            (attempt, max) => _logger.Information(
                "Placing stop loss for {Symbol} @ {StopPrice}, attempt {Attempt}/{Max}",
                symbol, stopPrice, attempt, max),
            (attempt, ex) => _logger.Warning(ex, "Stop loss placement attempt {Attempt} failed for {Symbol}", attempt, symbol),
            "Stop loss placement failed",
            throwOnFailure: false);
    }

    private async Task<ExecutionResult> PlaceTakeProfitOrderWithRetry(
        string symbol,
        SignalDirection direction,
        decimal quantity,
        decimal takeProfitPrice,
        CancellationToken ct)
    {
        var tradeDirection = direction == SignalDirection.Long
            ? TradeDirection.Long
            : TradeDirection.Short;

        return await ExecuteWithRetryAsync(
            ct,
            token => _orderExecutor.PlaceTakeProfitAsync(symbol, tradeDirection, quantity, takeProfitPrice, token),
            (attempt, max) => _logger.Information(
                "Placing take profit for {Symbol} @ {TpPrice}, attempt {Attempt}/{Max}",
                symbol, takeProfitPrice, attempt, max),
            (attempt, ex) => _logger.Warning(ex, "Take profit placement attempt {Attempt} failed for {Symbol}", attempt, symbol),
            "Take profit placement failed",
            throwOnFailure: false);
    }

    private async Task<ExecutionResult> ExecuteWithRetryAsync(
        CancellationToken ct,
        Func<CancellationToken, Task<ExecutionResult>> action,
        Action<int, int> logAttempt,
        Action<int, Exception> logFailure,
        string failureMessage,
        bool throwOnFailure)
    {
        var maxAttempts = RetryPolicySettings.MaxRetryAttempts;
        var attempt = 0;

        try
        {
            return await _retryPolicy.ExecuteAsync(
                async (context, token) =>
                {
                    attempt++;
                    logAttempt(attempt, maxAttempts);
                    return await action(token);
                },
                new Context
                {
                    ["OnRetry"] = (Action<Exception, int>)((exception, retryAttempt) =>
                    {
                        logFailure(retryAttempt, exception);
                    })
                },
                ct);
        }
        catch (Exception ex)
        {
            var finalAttempt = attempt == 0 ? maxAttempts : attempt;
            logFailure(finalAttempt, ex);

            if (throwOnFailure)
            {
                throw;
            }

            return new ExecutionResult
            {
                IsAcceptable = false,
                RejectReason = $"{failureMessage} after {maxAttempts} attempts: {ex.Message}"
            };
        }
    }
}
