using ComplexBot.Models;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.Trading;

namespace ComplexBot.Services.Backtesting;

public class MultiTimeframeBacktester
{
    private RiskManager? _riskManager;
    private BacktestSettings _settings = new();

    public MultiTimeframeBacktestResult Run(
        string symbol,
        List<Candle> primaryCandles,
        IStrategy primaryStrategy,
        IReadOnlyList<MultiTimeframeFilterDefinition> filters,
        RiskSettings riskSettings,
        BacktestSettings? backtestSettings = null)
    {
        if (primaryCandles.Count == 0)
            throw new ArgumentException("Primary candles are required for multi-timeframe backtest.");

        _settings = backtestSettings ?? new BacktestSettings();
        _riskManager = new RiskManager(riskSettings, _settings.InitialCapital);

        primaryStrategy.Reset();
        foreach (var filter in filters)
        {
            filter.Strategy.Reset();
        }

        decimal capital = _settings.InitialCapital;
        var position = new PositionState();
        var trades = new List<Trade>();
        var equityCurve = new List<decimal> { capital };

        int totalSignals = 0;
        int approvedSignals = 0;
        int blockedSignals = 0;

        var filterIndices = filters.Select(_ => 0).ToArray();

        for (int i = 0; i < primaryCandles.Count; i++)
        {
            var candle = primaryCandles[i];

            UpdateFilterStrategies(candle.CloseTime, filters, filterIndices, symbol);

            decimal unrealizedPnl = position.CalculateUnrealizedPnL(candle.Close);
            if (position.HasPosition)
            {
                position.UpdateExcursions(unrealizedPnl);
            }
            equityCurve.Add(capital + unrealizedPnl);
            _riskManager.UpdateEquity(capital + unrealizedPnl);

            if (position.HasPosition)
            {
                if (position.StopLoss.HasValue)
                {
                    var result = ExitConditionChecker.CheckStopLoss(
                        candle,
                        position.StopLoss.Value,
                        position.Direction!.Value,
                        price => ApplySlippage(price, position.Direction!.Value, false));

                    if (result.ShouldExit)
                    {
                        capital += ExecuteExit(position, result.ExitPrice, candle.OpenTime, result.Reason, symbol, trades);
                        continue;
                    }
                }

                if (position.TakeProfit.HasValue)
                {
                    var result = ExitConditionChecker.CheckTakeProfit(
                        candle,
                        position.TakeProfit.Value,
                        position.Direction!.Value,
                        price => ApplySlippage(price, position.Direction!.Value, false));

                    if (result.ShouldExit)
                    {
                        capital += ExecuteExit(position, result.ExitPrice, candle.OpenTime, result.Reason, symbol, trades);
                        continue;
                    }
                }
            }

            var signal = primaryStrategy.Analyze(candle, position.Position, symbol);

            if (position.HasPosition && primaryStrategy.CurrentStopLoss.HasValue)
            {
                position.UpdateStopLoss(primaryStrategy.CurrentStopLoss);
            }

            if (signal != null)
            {
                totalSignals++;
                var filterDecision = SignalFilterEvaluator.Evaluate(
                    signal,
                    filters.Select(f => (f.Filter, f.Strategy.GetCurrentState())).ToList());

                if (filterDecision.Approved)
                {
                    approvedSignals++;
                    var adjustedSignal = SignalFilterEvaluator.ApplyConfidenceAdjustment(
                        signal,
                        filterDecision.ConfidenceAdjustment);
                    capital += ProcessSignal(adjustedSignal, candle, position, symbol, trades, primaryStrategy);
                }
                else
                {
                    blockedSignals++;
                }
            }
        }

        if (position.HasPosition)
        {
            var lastCandle = primaryCandles[^1];
            decimal exitPrice = ApplySlippage(lastCandle.Close, position.Direction!.Value, false);
            capital += ExecuteExit(position, exitPrice, lastCandle.CloseTime, "End of Backtest", symbol, trades);
        }

        equityCurve.Add(capital);

        var avgInterval = CalculateAverageInterval(primaryCandles);
        var metrics = CalculateMetrics(
            trades,
            equityCurve,
            _settings.InitialCapital,
            primaryCandles.First().OpenTime,
            primaryCandles.Last().CloseTime,
            avgInterval);

        var backtestResult = new BacktestResult(
            $"{primaryStrategy.Name} (Multi-Timeframe)",
            primaryCandles.First().OpenTime,
            primaryCandles.Last().CloseTime,
            _settings.InitialCapital,
            capital,
            trades,
            equityCurve,
            metrics);

        return new MultiTimeframeBacktestResult(backtestResult, totalSignals, approvedSignals, blockedSignals);
    }

    private static void UpdateFilterStrategies(
        DateTime currentTime,
        IReadOnlyList<MultiTimeframeFilterDefinition> filters,
        int[] filterIndices,
        string symbol)
    {
        for (int i = 0; i < filters.Count; i++)
        {
            var filter = filters[i];
            var candles = filter.Candles;
            var index = filterIndices[i];

            while (index < candles.Count && candles[index].CloseTime <= currentTime)
            {
                filter.Strategy.Analyze(candles[index], 0, symbol);
                index++;
            }

            filterIndices[i] = index;
        }
    }

    private decimal ProcessSignal(
        TradeSignal signal,
        Candle candle,
        PositionState position,
        string symbol,
        List<Trade> trades,
        IStrategy primaryStrategy)
    {
        decimal pnl = 0;

        switch (signal.Type)
        {
            case SignalType.Buy when position.Position <= 0:
                pnl += CloseIfOpposite(position, candle, TradeDirection.Short, symbol, trades);
                OpenPosition(signal, candle, position, TradeDirection.Long, symbol, primaryStrategy);
                break;

            case SignalType.Sell when position.Position >= 0:
                pnl += CloseIfOpposite(position, candle, TradeDirection.Long, symbol, trades);
                OpenPosition(signal, candle, position, TradeDirection.Short, symbol, primaryStrategy);
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

    private decimal CloseIfOpposite(
        PositionState position,
        Candle candle,
        TradeDirection closeDirection,
        string symbol,
        List<Trade> trades)
    {
        if (!position.HasPosition || position.Direction != closeDirection)
            return 0;

        decimal exitPrice = ApplySlippage(candle.Close, closeDirection, false);
        return ExecuteExit(position, exitPrice, candle.OpenTime, "Signal Reversal", symbol, trades);
    }

    private void OpenPosition(
        TradeSignal signal,
        Candle candle,
        PositionState position,
        TradeDirection direction,
        string symbol,
        IStrategy primaryStrategy)
    {
        if (_riskManager == null || !_riskManager.CanOpenPosition() || !signal.StopLoss.HasValue)
            return;

        decimal entryPrice = ApplySlippage(candle.Close, direction, true);
        var sizing = _riskManager.CalculatePositionSize(
            entryPrice,
            signal.StopLoss.Value,
            primaryStrategy.CurrentAtr);

        if (sizing.Quantity <= 0)
            return;

        position.Open(
            sizing.Quantity,
            entryPrice,
            direction,
            candle.OpenTime,
            signal.StopLoss,
            signal.TakeProfit,
            sizing.RiskAmount);

        _riskManager.AddPosition(
            symbol,
            direction == TradeDirection.Long ? SignalType.Buy : SignalType.Sell,
            sizing.Quantity,
            sizing.RiskAmount,
            entryPrice,
            signal.StopLoss.Value,
            entryPrice);
    }

    private decimal ExecuteExit(
        PositionState position,
        decimal exitPrice,
        DateTime exitTime,
        string reason,
        string symbol,
        List<Trade> trades)
    {
        if (_riskManager == null)
            return 0;

        decimal pnl = position.CalculateExitPnL(exitPrice, position.AbsolutePosition);
        pnl -= CalculateFees(position.EntryPrice!.Value, position.AbsolutePosition);
        pnl -= CalculateFees(exitPrice, position.AbsolutePosition);

        trades.Add(new Trade(
            symbol,
            position.EntryTime!.Value,
            exitTime,
            position.EntryPrice!.Value,
            exitPrice,
            position.AbsolutePosition,
            position.Direction!.Value,
            position.StopLoss,
            position.TakeProfit,
            reason));

        position.Close();
        _riskManager.ClearPositions();

        return pnl;
    }

    private decimal ExecutePartialExit(
        TradeSignal signal,
        Candle candle,
        PositionState position,
        string symbol,
        List<Trade> trades)
    {
        if (_riskManager == null)
            return 0;

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
            position.EntryTime!.Value,
            candle.OpenTime,
            position.EntryPrice!.Value,
            exitPrice,
            exitQuantity,
            position.Direction!.Value,
            position.StopLoss,
            position.TakeProfit,
            signal.Reason));

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

        double years = (endDate - startDate).TotalDays / 365.25;
        decimal annualizedReturn = years > 0
            ? (decimal)(Math.Pow((double)(finalCapital / initialCapital), 1.0 / years) - 1) * 100
            : 0;

        var (maxDrawdown, maxDrawdownPercent) = CalculateMaxDrawdown(equityCurve, initialCapital);

        var returns = CalculateReturns(equityCurve);
        var (sharpeRatio, sortinoRatio) = CalculateRiskAdjustedReturns(returns, annualizedReturn, averageInterval);

        decimal grossProfit = winningTrades.Sum(t => t.PnL ?? 0);
        decimal grossLoss = Math.Abs(losingTrades.Sum(t => t.PnL ?? 0));
        decimal profitFactor = grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? 999 : 0;

        decimal winRate = completedTrades.Count > 0
            ? (decimal)winningTrades.Count / completedTrades.Count * 100
            : 0;

        decimal avgWin = winningTrades.Count > 0 ? winningTrades.Average(t => t.PnL ?? 0) : 0;
        decimal avgLoss = losingTrades.Count > 0 ? losingTrades.Average(t => t.PnL ?? 0) : 0;
        decimal largestWin = winningTrades.Count > 0 ? winningTrades.Max(t => t.PnL ?? 0) : 0;
        decimal largestLoss = losingTrades.Count > 0 ? losingTrades.Min(t => t.PnL ?? 0) : 0;

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
            avgHoldingPeriod);
    }

    private static (decimal maxDrawdown, decimal maxDrawdownPercent) CalculateMaxDrawdown(
        List<decimal> equityCurve,
        decimal initialCapital)
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

        var negativeReturns = returns.Where(r => r < 0).ToList();
        decimal downsideDev = negativeReturns.Count > 1
            ? (decimal)Math.Sqrt(negativeReturns.Select(r => (double)(r * r)).Average())
            : 0;

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
