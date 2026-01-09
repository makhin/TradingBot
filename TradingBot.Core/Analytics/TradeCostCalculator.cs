using TradingBot.Core.Models;

namespace TradingBot.Core.Analytics;

/// <summary>
/// Helper class for calculating trading costs (fees + slippage).
/// </summary>
public static class TradeCostCalculator
{
    public static decimal ApplySlippage(
        decimal price,
        decimal slippagePercent,
        TradeDirection direction,
        bool isEntry)
    {
        if (slippagePercent <= 0)
        {
            return price;
        }

        decimal slippageAmount = price * slippagePercent / 100m;
        bool isBuy = (direction == TradeDirection.Long && isEntry)
            || (direction == TradeDirection.Short && !isEntry);
        return isBuy ? price + slippageAmount : price - slippageAmount;
    }

    public static decimal CalculateFeesFromPercent(decimal price, decimal quantity, decimal feePercent)
    {
        if (feePercent <= 0)
        {
            return 0;
        }

        return price * quantity * feePercent / 100m;
    }

    public static decimal CalculateTotalCosts(
        decimal entryPrice,
        decimal exitPrice,
        decimal quantity,
        decimal feeRate,
        decimal slippageRate)
    {
        if (feeRate <= 0 && slippageRate <= 0)
        {
            return 0;
        }

        var notional = (entryPrice + exitPrice) * quantity;
        return notional * (feeRate + slippageRate);
    }
}
