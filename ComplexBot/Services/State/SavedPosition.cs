using System;
using ComplexBot.Models;

namespace ComplexBot.Services.State;

public record SavedPosition
{
    public string Symbol { get; init; } = "";
    public SignalType Direction { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal Quantity { get; init; }
    public decimal RemainingQuantity { get; init; }
    public decimal StopLoss { get; init; }
    public decimal TakeProfit { get; init; }
    public decimal RiskAmount { get; init; }
    public DateTime EntryTime { get; init; }
    public int TradeId { get; init; }
    public decimal CurrentPrice { get; init; }
    public bool BreakevenMoved { get; init; }
}
