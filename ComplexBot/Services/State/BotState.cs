using System;
using System.Collections.Generic;

namespace ComplexBot.Services.State;

public record BotState
{
    public DateTime LastUpdate { get; init; }
    public decimal CurrentEquity { get; init; }
    public decimal PeakEquity { get; init; }
    public decimal DayStartEquity { get; init; }
    public DateTime CurrentTradingDay { get; init; }
    public List<SavedPosition> OpenPositions { get; init; } = new();
    public List<SavedOcoOrder> ActiveOcoOrders { get; init; } = new();
    public int NextTradeId { get; init; }
    public string Symbol { get; init; } = "";
    public string Version { get; init; } = "1.0";
}
