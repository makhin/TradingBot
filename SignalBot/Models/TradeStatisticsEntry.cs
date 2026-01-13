namespace SignalBot.Models;

/// <summary>
/// Single closed trade statistics entry.
/// </summary>
public record TradeStatisticsEntry
{
    public required Guid PositionId { get; init; }
    public required string Symbol { get; init; }
    public required DateTime ClosedAt { get; init; }
    public required decimal RealizedPnl { get; init; }
}
