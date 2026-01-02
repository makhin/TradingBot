using ComplexBot.Models;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Analytics;

namespace ComplexBot.Services.Backtesting;

public class BacktestEngine
{
    private readonly IStrategy _strategy;
    private readonly RiskManager _riskManager;
    private readonly BacktestSettings _settings;
    private readonly TradeJournal? _journal;

    public BacktestEngine(IStrategy strategy, RiskSettings riskSettings, BacktestSettings? settings = null, TradeJournal? journal = null)
    {
        _strategy = strategy;
        _settings = settings ?? new BacktestSettings();
        _riskManager = new RiskManager(riskSettings, _settings.InitialCapital);
        _journal = journal;
    }

    public BacktestResult Run(List<Candle> candles, string symbol)
    {
        _strategy.Reset();
        _riskManager.ClearPositions();

        decimal capital = _settings.InitialCapital;
        var position = new PositionState();
        var trades = new List<Trade>();
        var equityCurve = new List<decimal> { capital };

        for (int i = 0; i < candles.Count; i++)
        {
            var candle = candles[i];

            // Update equity curve with unrealized PnL
            decimal unrealizedPnl = position.CalculateUnrealizedPnL(candle.Close);
            if (position.HasPosition)
            {
                position.UpdateExcursions(unrealizedPnl);
            }
            equityCurve.Add(capital + unrealizedPnl);
            _riskManager.UpdateEquity(capital + unrealizedPnl);

            // Check exit conditions
            if (position.HasPosition)
            {
                // Check stop loss
                if (position.StopLoss.HasValue)
                {
                    var result = ExitConditionChecker.CheckStopLoss(
                        candle, position.StopLoss.Value, position.Direction!.Value,
                        price => ApplySlippage(price, position.Direction!.Value, false));

                    if (result.ShouldExit)
                    {
                        capital += ExecuteExit(position, result.ExitPrice, candle.OpenTime, result.Reason, symbol, trades);
                        continue;
                    }
                }

                // Check take profit
                if (position.TakeProfit.HasValue)
                {
                    var result = ExitConditionChecker.CheckTakeProfit(
                        candle, position.TakeProfit.Value, position.Direction!.Value,
                        price => ApplySlippage(price, position.Direction!.Value, false));

                    if (result.ShouldExit)
                    {
                        capital += ExecuteExit(position, result.ExitPrice, candle.OpenTime, result.Reason, symbol, trades);
                        continue;
                    }
                }
            }

            // Get strategy signal
            var signal = _strategy.Analyze(candle, position.Position, symbol);
            if (signal != null)
            {
                capital += ProcessSignal(signal, candle, position, symbol, trades);
            }
        }

        // Close any remaining position at last price
        if (position.HasPosition && candles.Count > 0)
        {
            var lastCandle = candles[^1];
            decimal exitPrice = ApplySlippage(lastCandle.Close, position.Direction!.Value, false);
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
                decimal exitPrice = ApplySlippage(candle.Close, position.Direction!.Value, false);
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

        decimal exitPrice = ApplySlippage(candle.Close, closeDirection, false);
        return ExecuteExit(position, exitPrice, candle.OpenTime, "Signal Reversal", symbol, trades);
    }

    private void OpenPosition(TradeSignal signal, Candle candle, PositionState position, TradeDirection direction, string symbol)
    {
        if (!_riskManager.CanOpenPosition() || !signal.StopLoss.HasValue)
            return;

        decimal entryPrice = ApplySlippage(candle.Close, direction, true);
        var sizing = _riskManager.CalculatePositionSize(
            entryPrice, signal.StopLoss.Value,
            (_strategy as AdxTrendStrategy)?.CurrentAtr);

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
            var adxStrategy = _strategy as AdxTrendStrategy;
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
                AdxValue = adxStrategy?.CurrentAdx ?? 0,
                Atr = adxStrategy?.CurrentAtr ?? 0,
                EntryReason = signal.Reason
            });
        }
    }

    private decimal ExecuteExit(PositionState position, decimal exitPrice, DateTime exitTime, string reason, string symbol, List<Trade> trades)
    {
        decimal pnl = position.CalculateExitPnL(exitPrice, position.AbsolutePosition);
        pnl -= CalculateFees(position.EntryPrice!.Value, position.AbsolutePosition);
        pnl -= CalculateFees(exitPrice, position.AbsolutePosition);

        trades.Add(new Trade(
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
        decimal exitPrice = ApplySlippage(candle.Close, position.Direction!.Value, false);
        decimal pnl = position.CalculateExitPnL(exitPrice, exitQuantity);
        pnl -= CalculateFees(position.EntryPrice!.Value, exitQuantity);
        pnl -= CalculateFees(exitPrice, exitQuantity);

        trades.Add(new Trade(
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

    private decimal CalculateFees(decimal price, decimal quantity)
    {
        return price * quantity * _settings.CommissionPercent / 100;
    }

    private decimal ApplySlippage(decimal price, TradeDirection direction, bool isEntry)
    {
        decimal slippageAmount = price * _settings.SlippagePercent / 100;
        bool isBuy = (direction == TradeDirection.Long && isEntry)
            || (direction == TradeDirection.Short && !isEntry);
        return isBuy ? price + slippageAmount : price - slippageAmount;
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

public record BacktestSettings
{
    public decimal InitialCapital { get; init; } = 10000m;
    public decimal CommissionPercent { get; init; } = 0.1m;  // 0.1% Binance fee
    public decimal SlippagePercent { get; init; } = 0.05m;
}
