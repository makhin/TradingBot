using System;

namespace ComplexBot.Models;

public record Trade
{
    private readonly decimal _entryPrice;
    private readonly decimal _quantity;

    private Trade(
        string symbol,
        DateTime entryTime,
        DateTime? exitTime,
        decimal entryPrice,
        decimal? exitPrice,
        decimal quantity,
        TradeDirection direction,
        decimal? stopLoss,
        decimal? takeProfit,
        string? exitReason)
    {
        Symbol = symbol;
        EntryTime = entryTime;
        ExitTime = exitTime;
        EntryPrice = entryPrice;
        ExitPrice = exitPrice;
        Quantity = quantity;
        Direction = direction;
        StopLoss = stopLoss;
        TakeProfit = takeProfit;
        ExitReason = exitReason;
    }

    public string Symbol { get; init; }
    public DateTime EntryTime { get; init; }
    public DateTime? ExitTime { get; init; }
    public decimal EntryPrice
    {
        get => _entryPrice;
        init
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Entry price must be positive.");
            }

            _entryPrice = value;
        }
    }
    public decimal? ExitPrice { get; init; }
    public decimal Quantity
    {
        get => _quantity;
        init
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Quantity must be positive.");
            }

            _quantity = value;
        }
    }
    public TradeDirection Direction { get; init; }
    public decimal? StopLoss { get; init; }
    public decimal? TakeProfit { get; init; }
    public string? ExitReason { get; init; }

    public decimal? PnL => ExitPrice.HasValue
        ? Direction == TradeDirection.Long
            ? (ExitPrice.Value - EntryPrice) * Quantity
            : (EntryPrice - ExitPrice.Value) * Quantity
        : null;

    public decimal? PnLPercent => ExitPrice.HasValue
        ? Direction == TradeDirection.Long
            ? (ExitPrice.Value - EntryPrice) / EntryPrice * 100
            : (EntryPrice - ExitPrice.Value) / EntryPrice * 100
        : null;

    public static Trade Create(
        string symbol,
        DateTime entryTime,
        DateTime? exitTime,
        decimal entryPrice,
        decimal? exitPrice,
        decimal quantity,
        TradeDirection direction,
        decimal? stopLoss,
        decimal? takeProfit,
        string? exitReason)
    {
        return new Trade(
            symbol,
            entryTime,
            exitTime,
            entryPrice,
            exitPrice,
            quantity,
            direction,
            stopLoss,
            takeProfit,
            exitReason);
    }
}
