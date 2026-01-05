using ComplexBot.Models;

namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Manages position state during backtesting
/// Encapsulates position tracking, exit logic, and PnL calculations
/// </summary>
public class PositionState
{
    public decimal Position { get; private set; }
    public decimal? EntryPrice { get; private set; }
    public decimal? StopLoss { get; private set; }
    public decimal? TakeProfit { get; private set; }
    public DateTime? EntryTime { get; private set; }
    public TradeDirection? Direction { get; private set; }
    public int? JournalTradeId { get; set; }
    public decimal? PositionValue { get; private set; }
    public decimal? RiskAmount { get; private set; }

    // MAE/MFE tracking
    public decimal WorstPnL { get; private set; }
    public decimal BestPnL { get; private set; }
    public int BarsInTrade { get; private set; }

    public bool HasPosition => Position != 0;
    public bool IsLong => Position > 0;
    public bool IsShort => Position < 0;
    public decimal AbsolutePosition => Math.Abs(Position);

    /// <summary>
    /// Opens a new position
    /// </summary>
    public void Open(
        decimal quantity,
        decimal entryPrice,
        TradeDirection direction,
        DateTime entryTime,
        decimal? stopLoss,
        decimal? takeProfit,
        decimal riskAmount)
    {
        Position = direction == TradeDirection.Long ? quantity : -quantity;
        EntryPrice = entryPrice;
        Direction = direction;
        EntryTime = entryTime;
        StopLoss = stopLoss;
        TakeProfit = takeProfit;
        RiskAmount = riskAmount;
        PositionValue = entryPrice * quantity;
        WorstPnL = 0;
        BestPnL = 0;
        BarsInTrade = 0;
    }

    /// <summary>
    /// Closes the entire position
    /// </summary>
    public void Close()
    {
        Position = 0;
        EntryPrice = null;
        StopLoss = null;
        TakeProfit = null;
        EntryTime = null;
        Direction = null;
        JournalTradeId = null;
        PositionValue = null;
        RiskAmount = null;
        WorstPnL = 0;
        BestPnL = 0;
        BarsInTrade = 0;
    }

    /// <summary>
    /// Updates stop loss (for trailing stop sync from strategy)
    /// </summary>
    public void UpdateStopLoss(decimal? newStopLoss)
    {
        if (newStopLoss.HasValue && HasPosition)
        {
            StopLoss = newStopLoss;
        }
    }

    /// <summary>
    /// Updates position after partial exit
    /// </summary>
    public void PartialClose(decimal exitQuantity, decimal? newStopLoss = null)
    {
        decimal remaining = AbsolutePosition - exitQuantity;
        if (remaining <= 0)
        {
            Close();
        }
        else
        {
            Position = IsLong ? remaining : -remaining;
            if (newStopLoss.HasValue)
                StopLoss = newStopLoss;
        }
    }

    /// <summary>
    /// Updates MAE/MFE tracking with current unrealized PnL
    /// </summary>
    public void UpdateExcursions(decimal unrealizedPnl)
    {
        if (unrealizedPnl < WorstPnL) WorstPnL = unrealizedPnl;
        if (unrealizedPnl > BestPnL) BestPnL = unrealizedPnl;
        BarsInTrade++;
    }

    /// <summary>
    /// Calculates unrealized PnL at current price
    /// </summary>
    public decimal CalculateUnrealizedPnL(decimal currentPrice)
    {
        if (!EntryPrice.HasValue || Position == 0)
            return 0;

        return Direction == TradeDirection.Long
            ? (currentPrice - EntryPrice.Value) * AbsolutePosition
            : (EntryPrice.Value - currentPrice) * AbsolutePosition;
    }

    /// <summary>
    /// Calculates PnL for an exit at specified price
    /// </summary>
    public decimal CalculateExitPnL(decimal exitPrice, decimal quantity)
    {
        if (!EntryPrice.HasValue)
            return 0;

        return Direction == TradeDirection.Long
            ? (exitPrice - EntryPrice.Value) * quantity
            : (EntryPrice.Value - exitPrice) * quantity;
    }

    /// <summary>
    /// Gets the duration of the current trade
    /// </summary>
    public TimeSpan GetDuration(DateTime currentTime)
    {
        return EntryTime.HasValue ? currentTime - EntryTime.Value : TimeSpan.Zero;
    }
}
