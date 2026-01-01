using ComplexBot.Models;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.RiskManagement;

namespace ComplexBot.Services.Backtesting;

public class BacktestEngine
{
    private readonly IStrategy _strategy;
    private readonly RiskManager _riskManager;
    private readonly BacktestSettings _settings;
    
    public BacktestEngine(IStrategy strategy, RiskSettings riskSettings, BacktestSettings? settings = null)
    {
        _strategy = strategy;
        _settings = settings ?? new BacktestSettings();
        _riskManager = new RiskManager(riskSettings, _settings.InitialCapital);
    }

    public BacktestResult Run(List<Candle> candles)
    {
        _strategy.Reset();
        _riskManager.ClearPositions();

        decimal capital = _settings.InitialCapital;
        decimal position = 0;
        decimal? entryPrice = null;
        decimal? stopLoss = null;
        decimal? takeProfit = null;
        DateTime? entryTime = null;
        TradeDirection? direction = null;

        var trades = new List<Trade>();
        var equityCurve = new List<decimal> { capital };

        for (int i = 0; i < candles.Count; i++)
        {
            var candle = candles[i];
            
            // Calculate unrealized P&L for equity curve
            decimal unrealizedPnl = 0;
            if (position != 0 && entryPrice.HasValue)
            {
                unrealizedPnl = direction == TradeDirection.Long
                    ? (candle.Close - entryPrice.Value) * Math.Abs(position)
                    : (entryPrice.Value - candle.Close) * Math.Abs(position);
            }
            equityCurve.Add(capital + unrealizedPnl);
            _riskManager.UpdateEquity(capital + unrealizedPnl);

            // Check for stop loss hit (simulate intra-bar)
            if (position != 0 && stopLoss.HasValue)
            {
                bool stopHit = direction == TradeDirection.Long
                    ? candle.Low <= stopLoss.Value
                    : candle.High >= stopLoss.Value;

                if (stopHit)
                {
                    decimal exitPrice = ApplySlippage(stopLoss.Value, direction!.Value, isEntry: false);
                    decimal pnl = direction == TradeDirection.Long
                        ? (exitPrice - entryPrice!.Value) * Math.Abs(position)
                        : (entryPrice!.Value - exitPrice) * Math.Abs(position);

                    pnl -= CalculateFees(entryPrice!.Value, Math.Abs(position));
                    pnl -= CalculateFees(exitPrice, Math.Abs(position));

                    trades.Add(new Trade(
                        entryTime!.Value, candle.OpenTime,
                        entryPrice!.Value, exitPrice,
                        Math.Abs(position), direction!.Value,
                        stopLoss, takeProfit, "Stop Loss"
                    ));

                    capital += pnl;
                    position = 0;
                    entryPrice = null;
                    stopLoss = null;
                    takeProfit = null;
                    direction = null;
                    entryTime = null;
                    _riskManager.ClearPositions();
                }
            }

            // Check for take profit hit (simulate intra-bar)
            if (position != 0 && takeProfit.HasValue)
            {
                bool targetHit = direction == TradeDirection.Long
                    ? candle.High >= takeProfit.Value
                    : candle.Low <= takeProfit.Value;

                if (targetHit)
                {
                    decimal exitPrice = ApplySlippage(takeProfit.Value, direction!.Value, isEntry: false);
                    decimal pnl = direction == TradeDirection.Long
                        ? (exitPrice - entryPrice!.Value) * Math.Abs(position)
                        : (entryPrice!.Value - exitPrice) * Math.Abs(position);

                    pnl -= CalculateFees(entryPrice!.Value, Math.Abs(position));
                    pnl -= CalculateFees(exitPrice, Math.Abs(position));

                    trades.Add(new Trade(
                        entryTime!.Value, candle.OpenTime,
                        entryPrice!.Value, exitPrice,
                        Math.Abs(position), direction!.Value,
                        stopLoss, takeProfit, "Take Profit"
                    ));

                    capital += pnl;
                    position = 0;
                    entryPrice = null;
                    stopLoss = null;
                    takeProfit = null;
                    direction = null;
                    entryTime = null;
                    _riskManager.ClearPositions();
                }
            }

            // Get strategy signal
            var signal = _strategy.Analyze(candle, position);

            if (signal != null)
            {
                switch (signal.Type)
                {
                    case SignalType.Buy when position <= 0:
                        // Close short if exists
                        if (position < 0)
                        {
                            decimal exitPrice = ApplySlippage(candle.Close, TradeDirection.Short, isEntry: false);
                            decimal pnl = (entryPrice!.Value - exitPrice) * Math.Abs(position);
                            pnl -= CalculateFees(entryPrice!.Value, Math.Abs(position));
                            pnl -= CalculateFees(exitPrice, Math.Abs(position));

                            trades.Add(new Trade(
                                entryTime!.Value, candle.OpenTime,
                                entryPrice!.Value, exitPrice,
                                Math.Abs(position), TradeDirection.Short,
                                stopLoss, takeProfit, "Signal Reversal"
                            ));
                            capital += pnl;
                        }

                        // Open long if risk allows
                        if (_riskManager.CanOpenPosition() && signal.StopLoss.HasValue)
                        {
                            decimal adjustedEntryPrice = ApplySlippage(candle.Close, TradeDirection.Long, isEntry: true);
                            var sizing = _riskManager.CalculatePositionSize(
                                adjustedEntryPrice, signal.StopLoss.Value,
                                (_strategy as AdxTrendStrategy)?.CurrentAtr);

                            if (sizing.Quantity > 0)
                            {
                                position = sizing.Quantity;
                                entryPrice = adjustedEntryPrice;
                                stopLoss = signal.StopLoss;
                                takeProfit = signal.TakeProfit;
                                direction = TradeDirection.Long;
                                entryTime = candle.OpenTime;
                                _riskManager.AddPosition("BTCUSDT", position, sizing.RiskAmount, 
                                    adjustedEntryPrice, signal.StopLoss.Value);
                            }
                        }
                        break;

                    case SignalType.Sell when position >= 0:
                        // Close long if exists
                        if (position > 0)
                        {
                            decimal exitPrice = ApplySlippage(candle.Close, TradeDirection.Long, isEntry: false);
                            decimal pnl = (exitPrice - entryPrice!.Value) * position;
                            pnl -= CalculateFees(entryPrice!.Value, position);
                            pnl -= CalculateFees(exitPrice, position);

                            trades.Add(new Trade(
                                entryTime!.Value, candle.OpenTime,
                                entryPrice!.Value, exitPrice,
                                position, TradeDirection.Long,
                                stopLoss, takeProfit, "Signal Reversal"
                            ));
                            capital += pnl;
                        }

                        // Open short if risk allows
                        if (_riskManager.CanOpenPosition() && signal.StopLoss.HasValue)
                        {
                            decimal adjustedEntryPrice = ApplySlippage(candle.Close, TradeDirection.Short, isEntry: true);
                            var sizing = _riskManager.CalculatePositionSize(
                                adjustedEntryPrice, signal.StopLoss.Value,
                                (_strategy as AdxTrendStrategy)?.CurrentAtr);

                            if (sizing.Quantity > 0)
                            {
                                position = -sizing.Quantity;
                                entryPrice = adjustedEntryPrice;
                                stopLoss = signal.StopLoss;
                                takeProfit = signal.TakeProfit;
                                direction = TradeDirection.Short;
                                entryTime = candle.OpenTime;
                                _riskManager.AddPosition("BTCUSDT", sizing.Quantity, sizing.RiskAmount,
                                    adjustedEntryPrice, signal.StopLoss.Value);
                            }
                        }
                        break;

                    case SignalType.Exit when position != 0:
                        decimal exitPriceOnSignal = ApplySlippage(candle.Close, direction!.Value, isEntry: false);
                        decimal exitPnl = direction == TradeDirection.Long
                            ? (exitPriceOnSignal - entryPrice!.Value) * Math.Abs(position)
                            : (entryPrice!.Value - exitPriceOnSignal) * Math.Abs(position);
                        
                        exitPnl -= CalculateFees(entryPrice!.Value, Math.Abs(position));
                        exitPnl -= CalculateFees(exitPriceOnSignal, Math.Abs(position));

                        trades.Add(new Trade(
                            entryTime!.Value, candle.OpenTime,
                            entryPrice!.Value, exitPriceOnSignal,
                            Math.Abs(position), direction!.Value,
                            stopLoss, takeProfit, signal.Reason
                        ));

                        capital += exitPnl;
                        position = 0;
                        entryPrice = null;
                        stopLoss = null;
                        takeProfit = null;
                        direction = null;
                        entryTime = null;
                        _riskManager.ClearPositions();
                        break;
                }
            }
        }

        // Close any remaining position at last price
        if (position != 0 && candles.Count > 0)
        {
            var lastCandle = candles[^1];
            decimal finalExitPrice = ApplySlippage(lastCandle.Close, direction!.Value, isEntry: false);
            decimal finalPnl = direction == TradeDirection.Long
                ? (finalExitPrice - entryPrice!.Value) * Math.Abs(position)
                : (entryPrice!.Value - finalExitPrice) * Math.Abs(position);
            
            finalPnl -= CalculateFees(entryPrice!.Value, Math.Abs(position));
            finalPnl -= CalculateFees(finalExitPrice, Math.Abs(position));

            trades.Add(new Trade(
                entryTime!.Value, lastCandle.CloseTime,
                entryPrice!.Value, finalExitPrice,
                Math.Abs(position), direction!.Value,
                stopLoss, takeProfit, "End of Backtest"
            ));
            capital += finalPnl;
        }

        equityCurve.Add(capital);

        var metrics = CalculateMetrics(trades, equityCurve, _settings.InitialCapital,
            candles.First().OpenTime, candles.Last().CloseTime);

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
        DateTime endDate)
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

        // Returns for Sharpe/Sortino
        var returns = new List<decimal>();
        for (int i = 1; i < equityCurve.Count; i++)
        {
            if (equityCurve[i - 1] > 0)
                returns.Add((equityCurve[i] - equityCurve[i - 1]) / equityCurve[i - 1]);
        }

        decimal avgReturn = returns.Count > 0 ? returns.Average() : 0;
        decimal stdDev = returns.Count > 1 
            ? (decimal)Math.Sqrt(returns.Select(r => (double)((r - avgReturn) * (r - avgReturn))).Average())
            : 0;
        
        // Downside deviation for Sortino
        var negativeReturns = returns.Where(r => r < 0).ToList();
        decimal downsideDev = negativeReturns.Count > 1
            ? (decimal)Math.Sqrt(negativeReturns.Select(r => (double)(r * r)).Average())
            : 0;

        // Annualize (assuming daily data)
        decimal annualizedStdDev = stdDev * (decimal)Math.Sqrt(365);
        decimal annualizedDownsideDev = downsideDev * (decimal)Math.Sqrt(365);
        
        decimal sharpeRatio = annualizedStdDev > 0 ? annualizedReturn / 100 / annualizedStdDev : 0;
        decimal sortinoRatio = annualizedDownsideDev > 0 ? annualizedReturn / 100 / annualizedDownsideDev : 0;

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
}

public record BacktestSettings
{
    public decimal InitialCapital { get; init; } = 10000m;
    public decimal CommissionPercent { get; init; } = 0.1m;  // 0.1% Binance fee
    public decimal SlippagePercent { get; init; } = 0.05m;
}
