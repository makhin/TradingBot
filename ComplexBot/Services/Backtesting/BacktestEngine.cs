using TradingBot.Core.Models;
using ComplexBot.Services.Strategies;
using TradingBot.Core.RiskManagement;
using TradingBot.Core.Analytics;
using ComplexBot.Models;

namespace ComplexBot.Services.Backtesting;

public class BacktestEngine
{
    private readonly IStrategy _strategy;
    private readonly RiskManager _riskManager;
    private readonly BacktestSettings _settings;
    private readonly TradeJournal? _journal;
    private TradeSignal? _pendingSignal;

    public BacktestEngine(IStrategy strategy, RiskSettings riskSettings, BacktestSettings? settings = null, TradeJournal? journal = null)
    {
        _strategy = strategy;
        _settings = settings ?? new BacktestSettings();
        _riskManager = new RiskManager(riskSettings, _settings.InitialCapital);
        _journal = journal;
    }

    public BacktestResult Run(List<Candle> candles, string symbol)
    {
        // Validate input
        if (candles == null || candles.Count == 0)
        {
            throw new ArgumentException("Candle list cannot be null or empty", nameof(candles));
        }

        _strategy.Reset();
        _riskManager.ClearPositions();
        _pendingSignal = null;

        decimal capital = _settings.InitialCapital;
        var position = new PositionState();
        var trades = new List<Trade>();
        var equityCurve = new List<decimal> { capital };

        for (int i = 0; i < candles.Count; i++)
        {
            var candle = candles[i];

            // Execute pending signal from previous bar at current bar's open (fixes look-ahead bias)
            if (_pendingSignal != null && i > 0)
            {
                var entryCandle = new Candle(
                    candle.OpenTime,
                    candle.Open,
                    candle.Open,
                    candle.Open,
                    candle.Open,
                    0,
                    candle.OpenTime
                );
                capital += ProcessSignal(_pendingSignal, entryCandle, position, symbol, trades);
                _pendingSignal = null;
            }

            // Check exit conditions BEFORE updating unrealized PnL (fixes look-ahead bias)
            if (position.HasPosition)
            {
                var result = ExitConditionChecker.CheckExit(
                    candle,
                    position.StopLoss,
                    position.TakeProfit,
                    position.Direction!.Value,
                    price => TradeCostCalculator.ApplySlippage(
                        price,
                        _settings.SlippagePercent,
                        position.Direction!.Value,
                        false),
                    stopLossFirst: true);

                if (result.ShouldExit)
                {
                    capital += ExecuteExit(position, result.ExitPrice, candle.OpenTime, result.Reason, symbol, trades);
                    _pendingSignal = null; // Cancel any pending signal if position was closed
                }
            }

            // Update equity curve with unrealized PnL AFTER exit check
            decimal unrealizedPnl = position.CalculateUnrealizedPnL(candle.Close);
            if (position.HasPosition)
            {
                position.UpdateExcursions(unrealizedPnl);
            }
            equityCurve.Add(capital + unrealizedPnl);
            _riskManager.UpdateEquity(capital + unrealizedPnl);

            // Get strategy signal - will be executed at NEXT bar's open
            var signal = _strategy.Analyze(candle, position.Position, symbol);

            // Sync trailing stop from strategy to position
            if (position.HasPosition && _strategy.CurrentStopLoss.HasValue)
            {
                position.UpdateStopLoss(_strategy.CurrentStopLoss);
            }

            // Store signal for next bar execution (except Exit/PartialExit which execute immediately)
            if (signal != null)
            {
                if (signal.Type == SignalType.Exit || signal.Type == SignalType.PartialExit)
                {
                    // Exit signals execute immediately on current bar
                    capital += ProcessSignal(signal, candle, position, symbol, trades);
                }
                else
                {
                    // Entry signals execute at next bar's open
                    _pendingSignal = signal;
                }
            }
        }

        // Close any remaining position at last price
        if (position.HasPosition && candles.Count > 0)
        {
            var lastCandle = candles[^1];
            decimal exitPrice = TradeCostCalculator.ApplySlippage(
                lastCandle.Close,
                _settings.SlippagePercent,
                position.Direction!.Value,
                false);
            capital += ExecuteExit(position, exitPrice, lastCandle.CloseTime, "End of Backtest", symbol, trades);
        }

        equityCurve.Add(capital);

        var avgInterval = CalculateAverageInterval(candles);
        var metrics = CalculateMetrics(trades, equityCurve, _settings.InitialCapital,
            candles.First().OpenTime, candles.Last().CloseTime, avgInterval);

        return new BacktestResult(
            _strategy.Name,
            candles.First().OpenTime,
            candles.Last().CloseTime,
            _settings.InitialCapital,
            capital,
            trades,
            equityCurve,
            metrics
        );
    }

    private decimal ProcessSignal(TradeSignal signal, Candle candle, PositionState position, string symbol, List<Trade> trades)
    {
        decimal pnl = 0;

        switch (signal.Type)
        {
            case SignalType.Buy when position.Position <= 0:
                pnl += CloseIfOpposite(position, candle, TradeDirection.Short, symbol, trades);
                OpenPosition(signal, candle, position, TradeDirection.Long, symbol);
                break;

            case SignalType.Sell when position.Position >= 0:
                pnl += CloseIfOpposite(position, candle, TradeDirection.Long, symbol, trades);
                OpenPosition(signal, candle, position, TradeDirection.Short, symbol);
                break;

            case SignalType.Exit when position.HasPosition:
                decimal exitPrice = TradeCostCalculator.ApplySlippage(
                    candle.Close,
                    _settings.SlippagePercent,
                    position.Direction!.Value,
                    false);
                pnl += ExecuteExit(position, exitPrice, candle.OpenTime, signal.Reason, symbol, trades);
                break;

            case SignalType.PartialExit when position.HasPosition:
                pnl += ExecutePartialExit(signal, candle, position, symbol, trades);
                break;
        }

        return pnl;
    }

    private decimal CloseIfOpposite(PositionState position, Candle candle, TradeDirection closeDirection, string symbol, List<Trade> trades)
    {
        if (!position.HasPosition || position.Direction != closeDirection)
            return 0;

        decimal exitPrice = TradeCostCalculator.ApplySlippage(
            candle.Close,
            _settings.SlippagePercent,
            closeDirection,
            false);
        return ExecuteExit(position, exitPrice, candle.OpenTime, "Signal Reversal", symbol, trades);
    }

    private void OpenPosition(TradeSignal signal, Candle candle, PositionState position, TradeDirection direction, string symbol)
    {
        if (!_riskManager.CanOpenPosition() || !signal.StopLoss.HasValue)
            return;

        decimal entryPrice = TradeCostCalculator.ApplySlippage(
            candle.Close,
            _settings.SlippagePercent,
            direction,
            true);
        var sizing = _riskManager.CalculatePositionSize(
            entryPrice, signal.StopLoss.Value,
            _strategy.CurrentAtr);

        if (sizing.Quantity <= 0)
            return;

        position.Open(sizing.Quantity, entryPrice, direction, candle.OpenTime,
            signal.StopLoss, signal.TakeProfit, sizing.RiskAmount);

        _riskManager.AddPosition(symbol,
            direction == TradeDirection.Long ? SignalType.Buy : SignalType.Sell,
            sizing.Quantity, sizing.RiskAmount, entryPrice,
            signal.StopLoss.Value, entryPrice);

        if (_journal != null)
        {
            var indicatorSnapshotProvider = _strategy as IProvidesIndicatorSnapshot;
            position.JournalTradeId = _journal.OpenTrade(new TradeJournalEntry
            {
                EntryTime = candle.OpenTime,
                Symbol = symbol,
                Direction = direction == TradeDirection.Long ? SignalType.Buy : SignalType.Sell,
                EntryPrice = entryPrice,
                StopLoss = signal.StopLoss!.Value,
                TakeProfit = signal.TakeProfit ?? 0,
                Quantity = sizing.Quantity,
                PositionValueUsd = position.PositionValue ?? 0,
                RiskAmount = sizing.RiskAmount,
                Indicators = indicatorSnapshotProvider?.GetIndicatorSnapshot() ?? IndicatorSnapshot.Empty,
                EntryReason = signal.Reason
            });
        }
    }

    private decimal ExecuteExit(PositionState position, decimal exitPrice, DateTime exitTime, string reason, string symbol, List<Trade> trades)
    {
        decimal pnl = position.CalculateExitPnL(exitPrice, position.AbsolutePosition);
        pnl -= TradeCostCalculator.CalculateFeesFromPercent(
            position.EntryPrice!.Value,
            position.AbsolutePosition,
            _settings.CommissionPercent);
        pnl -= TradeCostCalculator.CalculateFeesFromPercent(
            exitPrice,
            position.AbsolutePosition,
            _settings.CommissionPercent);

        trades.Add(Trade.Create(
            symbol,
            position.EntryTime!.Value, exitTime,
            position.EntryPrice!.Value, exitPrice,
            position.AbsolutePosition, position.Direction!.Value,
            position.StopLoss, position.TakeProfit, reason
        ));

        CloseJournalTrade(position, exitTime, exitPrice, pnl, reason);

        position.Close();
        _riskManager.ClearPositions();

        return pnl;
    }

    private decimal ExecutePartialExit(TradeSignal signal, Candle candle, PositionState position, string symbol, List<Trade> trades)
    {
        decimal exitFraction = signal.PartialExitPercent ?? 0m;
        if (exitFraction > 1m)
            exitFraction /= 100m;

        decimal exitQuantity = signal.PartialExitQuantity ?? position.AbsolutePosition * exitFraction;
        if (exitQuantity <= 0)
            return 0;

        exitQuantity = Math.Min(exitQuantity, position.AbsolutePosition);
        decimal exitPrice = TradeCostCalculator.ApplySlippage(
            candle.Close,
            _settings.SlippagePercent,
            position.Direction!.Value,
            false);
        decimal pnl = position.CalculateExitPnL(exitPrice, exitQuantity);
        pnl -= TradeCostCalculator.CalculateFeesFromPercent(
            position.EntryPrice!.Value,
            exitQuantity,
            _settings.CommissionPercent);
        pnl -= TradeCostCalculator.CalculateFeesFromPercent(
            exitPrice,
            exitQuantity,
            _settings.CommissionPercent);

        trades.Add(Trade.Create(
            symbol,
            position.EntryTime!.Value, candle.OpenTime,
            position.EntryPrice!.Value, exitPrice,
            exitQuantity, position.Direction!.Value,
            position.StopLoss, position.TakeProfit, signal.Reason
        ));

        position.PartialClose(exitQuantity, signal.StopLoss);

        if (!position.HasPosition)
        {
            _riskManager.ClearPositions();
        }
        else
        {
            _riskManager.UpdatePositionAfterPartialExit(
                symbol,
                position.AbsolutePosition,
                position.StopLoss ?? position.EntryPrice!.Value,
                signal.MoveStopToBreakeven,
                exitPrice);
        }

        return pnl;
    }

    private void CloseJournalTrade(PositionState position, DateTime exitTime, decimal exitPrice, decimal pnl, string reason)
    {
        if (_journal == null || !position.JournalTradeId.HasValue)
            return;

        var result = pnl > 0.01m ? TradeResult.Win
            : pnl < -0.01m ? TradeResult.Loss
            : TradeResult.Breakeven;

        var rMultiple = position.RiskAmount > 0 ? pnl / position.RiskAmount.Value : 0;

        _journal.CloseTrade(position.JournalTradeId.Value, new TradeJournalEntry
        {
            ExitTime = exitTime,
            ExitPrice = exitPrice,
            GrossPnL = pnl,
            NetPnL = pnl,
            RMultiple = rMultiple,
            Result = result,
            ExitReason = reason,
            BarsInTrade = position.BarsInTrade,
            Duration = position.GetDuration(exitTime),
            MaxAdverseExcursion = position.WorstPnL,
            MaxFavorableExcursion = position.BestPnL
        });
    }

    private PerformanceMetrics CalculateMetrics(
        List<Trade> trades,
        List<decimal> equityCurve,
        decimal initialCapital,
        DateTime startDate,
        DateTime endDate,
        TimeSpan averageInterval)
    {
        if (trades.Count == 0)
        {
            return new PerformanceMetrics(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var completedTrades = trades.Where(t => t.PnL.HasValue).ToList();
        var winningTrades = completedTrades.Where(t => t.PnL > 0).ToList();
        var losingTrades = completedTrades.Where(t => t.PnL <= 0).ToList();

        decimal finalCapital = equityCurve.Last();
        decimal totalReturn = (finalCapital - initialCapital) / initialCapital * 100;

        // Annualized return
        double years = (endDate - startDate).TotalDays / 365.25;
        decimal annualizedReturn = years > 0
            ? (decimal)(Math.Pow((double)(finalCapital / initialCapital), 1.0 / years) - 1) * 100
            : 0;

        // Max drawdown
        var (maxDrawdown, maxDrawdownPercent) = CalculateMaxDrawdown(equityCurve, initialCapital);

        // Returns for Sharpe/Sortino
        var returns = CalculateReturns(equityCurve);
        var (sharpeRatio, sortinoRatio) = CalculateRiskAdjustedReturns(returns, annualizedReturn, averageInterval);

        // Profit factor
        decimal grossProfit = winningTrades.Sum(t => t.PnL ?? 0);
        decimal grossLoss = Math.Abs(losingTrades.Sum(t => t.PnL ?? 0));
        decimal profitFactor = grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? 999 : 0;

        // Win rate
        decimal winRate = completedTrades.Count > 0
            ? (decimal)winningTrades.Count / completedTrades.Count * 100
            : 0;

        // Average win/loss
        decimal avgWin = winningTrades.Count > 0 ? winningTrades.Average(t => t.PnL ?? 0) : 0;
        decimal avgLoss = losingTrades.Count > 0 ? losingTrades.Average(t => t.PnL ?? 0) : 0;
        decimal largestWin = winningTrades.Count > 0 ? winningTrades.Max(t => t.PnL ?? 0) : 0;
        decimal largestLoss = losingTrades.Count > 0 ? losingTrades.Min(t => t.PnL ?? 0) : 0;

        // Average holding period
        var holdingPeriods = completedTrades
            .Where(t => t.ExitTime.HasValue)
            .Select(t => t.ExitTime!.Value - t.EntryTime);
        var avgHoldingPeriod = holdingPeriods.Any()
            ? TimeSpan.FromTicks((long)holdingPeriods.Average(t => t.Ticks))
            : TimeSpan.Zero;

        return new PerformanceMetrics(
            totalReturn,
            annualizedReturn,
            maxDrawdown,
            maxDrawdownPercent,
            sharpeRatio,
            sortinoRatio,
            profitFactor,
            winRate,
            completedTrades.Count,
            winningTrades.Count,
            losingTrades.Count,
            avgWin,
            avgLoss,
            largestWin,
            largestLoss,
            avgHoldingPeriod
        );
    }

    private static (decimal maxDrawdown, decimal maxDrawdownPercent) CalculateMaxDrawdown(List<decimal> equityCurve, decimal initialCapital)
    {
        decimal peak = initialCapital;
        decimal maxDrawdown = 0;
        decimal maxDrawdownPercent = 0;

        foreach (var equity in equityCurve)
        {
            if (equity > peak) peak = equity;
            decimal drawdown = peak - equity;
            decimal drawdownPercent = peak > 0 ? drawdown / peak * 100 : 0;
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;
            if (drawdownPercent > maxDrawdownPercent) maxDrawdownPercent = drawdownPercent;
        }

        return (maxDrawdown, maxDrawdownPercent);
    }

    private static List<decimal> CalculateReturns(List<decimal> equityCurve)
    {
        var returns = new List<decimal>();
        for (int i = 1; i < equityCurve.Count; i++)
        {
            if (equityCurve[i - 1] > 0)
                returns.Add((equityCurve[i] - equityCurve[i - 1]) / equityCurve[i - 1]);
        }
        return returns;
    }

    private static (decimal sharpe, decimal sortino) CalculateRiskAdjustedReturns(
        List<decimal> returns,
        decimal annualizedReturn,
        TimeSpan averageInterval)
    {
        if (returns.Count == 0)
            return (0, 0);

        decimal avgReturn = returns.Average();
        decimal stdDev = returns.Count > 1
            ? (decimal)Math.Sqrt(returns.Select(r => (double)((r - avgReturn) * (r - avgReturn))).Average())
            : 0;

        // Downside deviation for Sortino
        var negativeReturns = returns.Where(r => r < 0).ToList();
        decimal downsideDev = negativeReturns.Count > 1
            ? (decimal)Math.Sqrt(negativeReturns.Select(r => (double)(r * r)).Average())
            : 0;

        // Annualize based on average interval
        double averageIntervalDays = averageInterval.TotalDays;
        double periodsPerYear = averageIntervalDays > 0 ? 365.0 / averageIntervalDays : 365.0;
        decimal annualizationFactor = (decimal)Math.Sqrt(periodsPerYear);
        decimal annualizedStdDev = stdDev * annualizationFactor;
        decimal annualizedDownsideDev = downsideDev * annualizationFactor;

        decimal sharpeRatio = annualizedStdDev > 0 ? annualizedReturn / 100 / annualizedStdDev : 0;
        decimal sortinoRatio = annualizedDownsideDev > 0 ? annualizedReturn / 100 / annualizedDownsideDev : 0;

        return (sharpeRatio, sortinoRatio);
    }

    private static TimeSpan CalculateAverageInterval(List<Candle> candles)
    {
        if (candles.Count < 2)
            return TimeSpan.FromDays(1);

        long totalTicks = 0;
        int intervalCount = 0;

        for (int i = 1; i < candles.Count; i++)
        {
            var interval = candles[i].OpenTime - candles[i - 1].OpenTime;
            if (interval.Ticks > 0)
            {
                totalTicks += interval.Ticks;
                intervalCount++;
            }
        }

        return intervalCount == 0
            ? TimeSpan.FromDays(1)
            : TimeSpan.FromTicks(totalTicks / intervalCount);
    }
}
