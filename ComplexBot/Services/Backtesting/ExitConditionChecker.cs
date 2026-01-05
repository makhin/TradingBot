using System;
using ComplexBot.Models;

namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Helper class for checking exit conditions
/// </summary>
public static class ExitConditionChecker
{
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
