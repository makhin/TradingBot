namespace SignalBot.Models;

public record TradeStatisticsReport
{
    public required DateTime GeneratedAt { get; init; }
    public required IReadOnlyList<TradeStatisticsWindowReport> Windows { get; init; }
}

public record TradeStatisticsWindowReport
{
    public required string Name { get; init; }
    public required TimeSpan Duration { get; init; }
    public required int TradeCount { get; init; }
    public required decimal Profit { get; init; }
    public required decimal Loss { get; init; }
    public required decimal Net { get; init; }
}
