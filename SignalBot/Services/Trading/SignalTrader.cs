using SignalBot.Configuration;
using SignalBot.Models;
using TradingBot.Binance.Futures.Interfaces;
using TradingBot.Binance.Futures.Models;
using TradingBot.Core.Models;
using TradingBot.Core.RiskManagement;
using Serilog;

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
    private readonly ILogger _logger;

    public SignalTrader(
        IBinanceFuturesClient client,
        IFuturesOrderExecutor orderExecutor,
        IPositionManager positionManager,
        IRiskManager riskManager,
        TradingSettings settings,
        ILogger? logger = null)
    {
        _client = client;
        _orderExecutor = orderExecutor;
        _positionManager = positionManager;
        _riskManager = riskManager;
        _settings = settings;
        _logger = logger ?? Log.ForContext<SignalTrader>();
    }

    public async Task<SignalPosition> ExecuteSignalAsync(
        TradingSignal signal,
        decimal accountEquity,
        CancellationToken ct = default)
    {
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

            // 3. Calculate position size
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

            if (slResult.OrderId.HasValue)
            {
                position = position with { StopLossOrderId = slResult.OrderId.Value };
            }
            else
            {
                _logger.Warning("Failed to place stop loss for {Symbol}: {Reason}",
                    signal.Symbol, slResult.RejectReason);
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
            decimal percentToClose = percents[i];
            decimal quantityToClose = totalQuantity * (percentToClose / 100m);

            decimal? moveSlTo = null;
            if (_settings.MoveStopToBreakeven)
            {
                moveSlTo = i == 0
                    ? signal.Entry  // After T1, move SL to breakeven
                    : signal.Targets[i - 1];  // After T2, move to T1, etc.
            }

            targets.Add(new TargetLevel
            {
                Index = i,
                Price = signal.Targets[i],
                PercentToClose = percentToClose,
                QuantityToClose = quantityToClose,
                MoveStopLossTo = moveSlTo
            });
        }

        return targets;
    }

    private async Task<TradingBot.Binance.Common.Models.ExecutionResult> PlaceMarketOrderWithRetry(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        CancellationToken ct,
        int maxRetries = 3)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.Information("Placing market {Direction} order for {Symbol}, attempt {Attempt}/{Max}",
                    direction, symbol, attempt, maxRetries);

                return await _orderExecutor.PlaceMarketOrderAsync(symbol, direction, quantity, ct);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Market order attempt {Attempt} failed for {Symbol}", attempt, symbol);

                if (attempt == maxRetries)
                    throw;

                await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
            }
        }

        return new TradingBot.Binance.Common.Models.ExecutionResult
        {
            IsAcceptable = false,
            RejectReason = "Max retries exceeded"
        };
    }

    private async Task<TradingBot.Binance.Common.Models.ExecutionResult> PlaceStopLossOrderWithRetry(
        string symbol,
        SignalDirection direction,
        decimal quantity,
        decimal stopPrice,
        CancellationToken ct,
        int maxRetries = 3)
    {
        var tradeDirection = direction == SignalDirection.Long
            ? TradeDirection.Long
            : TradeDirection.Short;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.Information("Placing stop loss for {Symbol} @ {StopPrice}, attempt {Attempt}/{Max}",
                    symbol, stopPrice, attempt, maxRetries);

                return await _orderExecutor.PlaceStopLossAsync(symbol, tradeDirection, quantity, stopPrice, ct);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Stop loss placement attempt {Attempt} failed for {Symbol}", attempt, symbol);

                if (attempt == maxRetries)
                {
                    return new TradingBot.Binance.Common.Models.ExecutionResult
                    {
                        IsAcceptable = false,
                        RejectReason = $"Stop loss placement failed after {maxRetries} attempts: {ex.Message}"
                    };
                }

                await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
            }
        }

        return new TradingBot.Binance.Common.Models.ExecutionResult
        {
            IsAcceptable = false,
            RejectReason = "Max retries exceeded"
        };
    }

    private async Task<TradingBot.Binance.Common.Models.ExecutionResult> PlaceTakeProfitOrderWithRetry(
        string symbol,
        SignalDirection direction,
        decimal quantity,
        decimal takeProfitPrice,
        CancellationToken ct,
        int maxRetries = 3)
    {
        var tradeDirection = direction == SignalDirection.Long
            ? TradeDirection.Long
            : TradeDirection.Short;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.Information("Placing take profit for {Symbol} @ {TpPrice}, attempt {Attempt}/{Max}",
                    symbol, takeProfitPrice, attempt, maxRetries);

                return await _orderExecutor.PlaceTakeProfitAsync(symbol, tradeDirection, quantity, takeProfitPrice, ct);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Take profit placement attempt {Attempt} failed for {Symbol}", attempt, symbol);

                if (attempt == maxRetries)
                {
                    return new TradingBot.Binance.Common.Models.ExecutionResult
                    {
                        IsAcceptable = false,
                        RejectReason = $"Take profit placement failed after {maxRetries} attempts: {ex.Message}"
                    };
                }

                await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
            }
        }

        return new TradingBot.Binance.Common.Models.ExecutionResult
        {
            IsAcceptable = false,
            RejectReason = "Max retries exceeded"
        };
    }
}
