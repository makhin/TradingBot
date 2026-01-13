namespace SignalBot.Models;

public record TradeStatisticsWindow
{
    public required string Name { get; init; }
    public required TimeSpan Duration { get; init; }
}
