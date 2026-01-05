using System;

namespace ComplexBot.Models;

public record Trade(
    string Symbol,
    DateTime EntryTime,
    DateTime? ExitTime,
    decimal EntryPrice,
    decimal? ExitPrice,
    decimal Quantity,
    TradeDirection Direction,
    decimal? StopLoss,
    decimal? TakeProfit,
    string? ExitReason
)
{
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
}
