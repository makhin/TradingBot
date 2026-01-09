namespace TradingBot.Binance.Futures.Models;

/// <summary>
/// Represents a Binance Futures position
/// </summary>
public record FuturesPosition
{
    public required string Symbol { get; init; }
    public required PositionSide Side { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal EntryPrice { get; init; }
    public required decimal MarkPrice { get; init; }
    public required decimal UnrealizedPnl { get; init; }
    public required decimal LiquidationPrice { get; init; }
    public required int Leverage { get; init; }
    public required MarginType MarginType { get; init; }
    public required decimal InitialMargin { get; init; }
    public required decimal MaintMargin { get; init; }
}

/// <summary>
/// Position side for Futures (Long/Short)
/// </summary>
public enum PositionSide
{
    Long,
    Short,
    Both  // For hedge mode
}

/// <summary>
/// Margin type for Futures positions
/// </summary>
public enum MarginType
{
    Isolated,
    Cross
}
