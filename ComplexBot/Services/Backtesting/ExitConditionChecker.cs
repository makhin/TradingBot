using System;
using ComplexBot.Models;

namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Helper class for checking exit conditions
/// </summary>
public static class ExitConditionChecker
{
    /// <summary>
    /// Checks stop loss and take profit with a configurable priority.
    /// </summary>
    public static ExitCheckResult CheckExit(
        Candle candle,
        decimal? stopLoss,
        decimal? takeProfit,
        TradeDirection direction,
        Func<decimal, decimal> applySlippage,
        bool stopLossFirst = true)
    {
        if (stopLossFirst && stopLoss.HasValue)
        {
            var stopResult = CheckStopLoss(candle, stopLoss.Value, direction, applySlippage);
            if (stopResult.ShouldExit)
            {
                return stopResult;
            }
        }

        if (takeProfit.HasValue)
        {
            var takeProfitResult = CheckTakeProfit(candle, takeProfit.Value, direction, applySlippage);
            if (takeProfitResult.ShouldExit)
            {
                return takeProfitResult;
            }
        }

        if (!stopLossFirst && stopLoss.HasValue)
        {
            return CheckStopLoss(candle, stopLoss.Value, direction, applySlippage);
        }

        return new ExitCheckResult(false, 0, string.Empty);
    }

    /// <summary>
    /// Checks if stop loss is hit
    /// </summary>
    public static ExitCheckResult CheckStopLoss(
        Candle candle,
        decimal stopLoss,
        TradeDirection direction,
        Func<decimal, decimal> applySlippage)
    {
        bool stopHit = direction == TradeDirection.Long
            ? candle.Low <= stopLoss
            : candle.High >= stopLoss;

        if (stopHit)
        {
            var exitPrice = applySlippage(stopLoss);
            return new ExitCheckResult(true, exitPrice, "Stop Loss");
        }

        return new ExitCheckResult(false, 0, string.Empty);
    }

    /// <summary>
    /// Checks if take profit is hit
    /// </summary>
    public static ExitCheckResult CheckTakeProfit(
        Candle candle,
        decimal takeProfit,
        TradeDirection direction,
        Func<decimal, decimal> applySlippage)
    {
        bool targetHit = direction == TradeDirection.Long
            ? candle.High >= takeProfit
            : candle.Low <= takeProfit;

        if (targetHit)
        {
            var exitPrice = applySlippage(takeProfit);
            return new ExitCheckResult(true, exitPrice, "Take Profit");
        }

        return new ExitCheckResult(false, 0, string.Empty);
    }
}
