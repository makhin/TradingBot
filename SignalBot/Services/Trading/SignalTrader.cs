using System.Diagnostics;
using System.Text.RegularExpressions;
using SignalBot.Configuration;
using SignalBot.Models;
using SignalBot.Telemetry;
using TradingBot.Core.Exchanges;
using TradingBot.Core.Models;
using TradingBot.Core.RiskManagement;
using Serilog;
using Polly;
using Serilog.Context;

namespace SignalBot.Services.Trading;

/// <summary>
/// Executes trading signals on configured futures exchange
/// </summary>
public class SignalTrader : ISignalTrader
{
    private static readonly Regex MaxQuantityRegex = new(
        @"(?:maximum quantity[^\d]*|max(?:imum)?\s+quantity[^\d]*)([\d,]+(?:\.\d+)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IFuturesExchangeClient _client;
    private readonly IFuturesOrderExecutor _orderExecutor;
    private readonly IPositionManager _positionManager;
    private readonly IRiskManager _riskManager;
    private readonly TradingSettings _settings;
    private readonly EntrySettings _entrySettings;
    private readonly PositionSizingSettings _positionSizing;
    private readonly ILogger _logger;
    private readonly IAsyncPolicy<ExecutionResult> _retryPolicy;

    public SignalTrader(
        IFuturesExchangeClient client,
        IFuturesOrderExecutor orderExecutor,
        IPositionManager positionManager,
        IRiskManager riskManager,
        TradingSettings settings,
        EntrySettings entrySettings,
        PositionSizingSettings positionSizing,
        IAsyncPolicy<ExecutionResult> retryPolicy,
        ILogger? logger = null)
    {
        _client = client;
        _orderExecutor = orderExecutor;
        _positionManager = positionManager;
        _riskManager = riskManager;
        _settings = settings;
        _entrySettings = entrySettings;
        _positionSizing = positionSizing;
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
            // 2. Check price deviation
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
            _riskManager.UpdateEquity(accountEquity);

            var positionSize = CalculatePositionSize(signal);

            var adjustedQuantity = ApplyPositionSizeLimits(positionSize.Quantity, signal.Entry, accountEquity, signal.Symbol);

            if (adjustedQuantity <= 0)
            {
                _logger.Warning(
                    "Signal execution skipped for {Symbol}: calculated quantity is zero after position sizing limits. " +
                    "Check PositionSizing settings (DefaultFixedAmount, MinPositionUsdt, MaxPositionUsdt, MaxPositionPercent) and account balance.",
                    signal.Symbol);

                position = position with { Status = PositionStatus.Cancelled };
                await _positionManager.SavePositionAsync(position, ct);
                return position;
            }

            // 5. Set leverage and margin type only for actionable signals
            var leverageSet = await _client.SetLeverageAsync(signal.Symbol, signal.AdjustedLeverage, ct);
            if (!leverageSet)
            {
                _logger.Warning("Failed to set leverage for {Symbol}, continuing anyway", signal.Symbol);
            }

            // Parse margin type from settings instead of hardcoding
            var marginType = Enum.Parse<MarginType>(_settings.MarginType, ignoreCase: true);
            var marginTypeSet = await _client.SetMarginTypeAsync(signal.Symbol, marginType, ct);
            if (!marginTypeSet)
            {
                _logger.Warning("Failed to set margin type for {Symbol}, may already be set", signal.Symbol);
            }

            position = position with
            {
                InitialQuantity = adjustedQuantity,
                RemainingQuantity = adjustedQuantity,
                Status = PositionStatus.Opening,
                Targets = CreateTargetLevels(signal, adjustedQuantity)
            };
            await _positionManager.SavePositionAsync(position, ct);

            // 6. Open position (Market order)
            var tradeDirection = signal.Direction == SignalDirection.Long
                ? TradeDirection.Long
                : TradeDirection.Short;

            var (entryResult, entryQuantity) = await PlaceMarketOrderWithQuantityFallback(
                signal.Symbol,
                tradeDirection,
                adjustedQuantity,
                ct);

            if (!entryResult.Success || entryResult.OrderId == 0)
            {
                position = position with { Status = PositionStatus.Failed };
                await _positionManager.SavePositionAsync(position, ct);
                throw new InvalidOperationException($"Entry order failed: {entryResult.ErrorMessage}");
            }

            if (entryQuantity != position.InitialQuantity)
            {
                _logger.Warning(
                    "Entry quantity for {Symbol} was adjusted from {OriginalQty} to {AdjustedQty} due to exchange limits",
                    signal.Symbol,
                    position.InitialQuantity,
                    entryQuantity);

                position = position with
                {
                    InitialQuantity = entryQuantity,
                    RemainingQuantity = entryQuantity,
                    Targets = CreateTargetLevels(signal, entryQuantity)
                };
            }

            position = position with
            {
                EntryOrderId = entryResult.OrderId,
                ActualEntryPrice = entryResult.AveragePrice,
                OpenedAt = DateTime.UtcNow,
                Status = PositionStatus.Open
            };

            // 7. Place Stop Loss order
            var slResult = await PlaceStopLossOrderWithRetry(
                signal.Symbol,
                signal.Direction,
                entryQuantity,
                signal.AdjustedStopLoss,
                ct);

            if (slResult.Success && slResult.OrderId != 0)
            {
                position = position with { StopLossOrderId = slResult.OrderId };
            }
            else
            {
                _logger.Error("Stop loss placement failed for {Symbol}: {Reason}. Closing position at market.",
                    signal.Symbol, slResult.ErrorMessage);

                var closeDirection = tradeDirection == TradeDirection.Long
                    ? TradeDirection.Short
                    : TradeDirection.Long;
                var closeResult = await _orderExecutor.PlaceMarketOrderAsync(
                    signal.Symbol,
                    closeDirection,
                    entryQuantity,
                    ct);

                if (!closeResult.Success)
                {
                    position = position with { Status = PositionStatus.Failed };
                    await _positionManager.SavePositionAsync(position, ct);
                    throw new InvalidOperationException(
                        $"Stop loss placement failed and market close failed: {closeResult.ErrorMessage}");
                }

                var realizedPnl = closeResult.AveragePrice > 0
                    ? PnlCalculator.Calculate(
                        position.ActualEntryPrice,
                        closeResult.AveragePrice,
                        entryQuantity,
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
                    $"Stop loss placement failed; position closed at market: {slResult.ErrorMessage}");
            }

            // 8. Place Take Profit orders for each target
            var tpOrderIds = new List<long>();
            foreach (var target in position.Targets)
            {
                var tpResult = await PlaceTakeProfitOrderWithRetry(
                    signal.Symbol,
                    signal.Direction,
                    target.QuantityToClose,
                    target.Price,
                    ct);

                if (tpResult.Success && tpResult.OrderId != 0)
                {
                    tpOrderIds.Add(tpResult.OrderId);
                }
                else
                {
                    _logger.Warning("Failed to place take profit for target {Index} on {Symbol}: {Reason}",
                        target.Index, signal.Symbol, tpResult.ErrorMessage);
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

    private PositionSizeResult CalculatePositionSize(TradingSignal signal)
    {
        var mode = (_positionSizing.DefaultMode ?? string.Empty).Trim();

        if (mode.Equals("FixedAmount", StringComparison.OrdinalIgnoreCase))
        {
            var notional = ResolveFixedAmountNotional(signal.Symbol);
            var quantity = signal.Entry > 0 ? notional / signal.Entry : 0m;
            return new PositionSizeResult(quantity, 0m, 0m);
        }

        if (mode.Equals("FixedMargin", StringComparison.OrdinalIgnoreCase))
        {
            var margin = _positionSizing.DefaultFixedMargin;
            var notional = margin * signal.AdjustedLeverage;
            var quantity = signal.Entry > 0 ? notional / signal.Entry : 0m;
            return new PositionSizeResult(quantity, 0m, 0m);
        }

        // Default and fallback: RiskPercent
        return _riskManager.CalculatePositionSize(signal.Entry, signal.AdjustedStopLoss);
    }

    private decimal ResolveFixedAmountNotional(string symbol)
    {
        if (_positionSizing.SymbolOverrides.TryGetValue(symbol, out var symbolOverride) && symbolOverride.FixedAmount.HasValue)
        {
            return symbolOverride.FixedAmount.Value;
        }

        return _positionSizing.DefaultFixedAmount;
    }

    private decimal ApplyPositionSizeLimits(decimal quantity, decimal entryPrice, decimal accountEquity, string symbol)
    {
        if (quantity <= 0 || entryPrice <= 0)
            return 0;

        var maxNotionalByUsdt = _positionSizing.Limits.MaxPositionUsdt;
        var maxNotionalByPercent = accountEquity * (_positionSizing.Limits.MaxPositionPercent / 100m);
        var allowedNotional = Math.Min(maxNotionalByUsdt, maxNotionalByPercent);

        if (allowedNotional <= 0)
        {
            _logger.Warning(
                "Position size blocked for {Symbol}: allowed notional is {Limit:F4} USDT (balance/limits too restrictive)",
                symbol,
                allowedNotional);
            return 0;
        }

        var requestedNotional = quantity * entryPrice;
        if (requestedNotional <= allowedNotional)
        {
            var minNotional = _positionSizing.Limits.MinPositionUsdt;
            if (requestedNotional < minNotional)
            {
                _logger.Warning(
                    "Position size blocked for {Symbol}: requested notional {Notional:F4} USDT is below minimum {Min:F4} USDT",
                    symbol,
                    requestedNotional,
                    minNotional);
                return 0;
            }

            return quantity;
        }

        var cappedQuantity = allowedNotional / entryPrice;
        _logger.Warning(
            "Position size capped for {Symbol}: requested notional {Requested:F4} USDT exceeds limit {Limit:F4} USDT",
            symbol,
            requestedNotional,
            allowedNotional);

        var cappedMinNotional = _positionSizing.Limits.MinPositionUsdt;
        if (cappedQuantity * entryPrice < cappedMinNotional)
        {
            _logger.Warning(
                "Position size blocked for {Symbol}: capped notional {Notional:F4} USDT is below minimum {Min:F4} USDT",
                symbol,
                cappedQuantity * entryPrice,
                cappedMinNotional);
            return 0;
        }

        return cappedQuantity;
    }

    private async Task<(ExecutionResult Result, decimal Quantity)> PlaceMarketOrderWithQuantityFallback(
        string symbol,
        TradeDirection direction,
        decimal requestedQuantity,
        CancellationToken ct)
    {
        var firstAttempt = await PlaceMarketOrderWithRetry(symbol, direction, requestedQuantity, ct);
        if (firstAttempt.Success)
        {
            return (firstAttempt, requestedQuantity);
        }

        if (!TryExtractMaxQuantity(firstAttempt.ErrorMessage, out var maxQuantity) || maxQuantity <= 0 || maxQuantity >= requestedQuantity)
        {
            return (firstAttempt, requestedQuantity);
        }

        _logger.Warning(
            "Exchange rejected quantity {RequestedQty} for {Symbol}, retrying with max allowed quantity {MaxQty}",
            requestedQuantity,
            symbol,
            maxQuantity);

        var secondAttempt = await PlaceMarketOrderWithRetry(symbol, direction, maxQuantity, ct);
        return (secondAttempt, secondAttempt.Success ? maxQuantity : requestedQuantity);
    }

    private static bool TryExtractMaxQuantity(string? errorMessage, out decimal maxQuantity)
    {
        maxQuantity = 0;
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return false;
        }

        var match = MaxQuantityRegex.Match(errorMessage);
        if (!match.Success)
        {
            return false;
        }

        var rawValue = match.Groups[1].Value.Replace(",", string.Empty);
        return decimal.TryParse(rawValue, out maxQuantity);
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
                Success = false,
                ErrorMessage = $"{failureMessage} after {maxAttempts} attempts: {ex.Message}"
            };
        }
    }
}
