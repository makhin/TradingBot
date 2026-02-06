namespace TradingBot.Core.Models;

/// <summary>
/// Exchange-agnostic order execution update event
/// </summary>
public record OrderUpdate
{
    public required string Symbol { get; init; }
    public required long OrderId { get; init; }
    public required string Status { get; init; }
    public required TradeDirection Direction { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal Price { get; init; }
    public required decimal AveragePrice { get; init; }
    public required decimal QuantityFilled { get; init; }
    public required DateTime UpdateTime { get; init; }
    public string? OrderType { get; init; }
    public string? TimeInForce { get; init; }
}

/// <summary>
/// Exchange-agnostic position update event (Futures only)
/// </summary>
public record PositionUpdate
{
    public required string Symbol { get; init; }
    public required decimal PositionAmount { get; init; }
    public required decimal EntryPrice { get; init; }
    public required decimal UnrealizedPnl { get; init; }
    public required DateTime UpdateTime { get; init; }
    public TradeDirection? Side { get; init; }
}

/// <summary>
/// Exchange-agnostic account balance update event
/// </summary>
public record AccountUpdate
{
    public required string Asset { get; init; }
    public required decimal Balance { get; init; }
    public required decimal AvailableBalance { get; init; }
    public required DateTime UpdateTime { get; init; }
}
