namespace TradingBot.Indicators.Utils;

/// <summary>
/// Helper class for calculating PnL
/// </summary>
public static class PnLCalculator
{
    /// <summary>
    /// Calculates profit/loss for a position
    /// </summary>
    public static decimal Calculate(decimal entryPrice, decimal exitPrice, decimal quantity, bool isLong)
    {
        return isLong
            ? (exitPrice - entryPrice) * quantity
            : (entryPrice - exitPrice) * quantity;
    }

    /// <summary>
    /// Calculates profit/loss with fees
    /// </summary>
    public static decimal CalculateNet(
        decimal entryPrice,
        decimal exitPrice,
        decimal quantity,
        bool isLong,
        decimal feePercent)
    {
        decimal grossPnl = Calculate(entryPrice, exitPrice, quantity, isLong);
        decimal entryFee = entryPrice * quantity * feePercent / 100;
        decimal exitFee = exitPrice * quantity * feePercent / 100;
        return grossPnl - entryFee - exitFee;
    }
}
