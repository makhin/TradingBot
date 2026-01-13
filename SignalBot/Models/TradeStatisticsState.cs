namespace SignalBot.Models;

/// <summary>
/// Persisted trade statistics state.
/// </summary>
public record TradeStatisticsState
{
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;
    public IReadOnlyList<TradeStatisticsEntry> Entries { get; init; } = Array.Empty<TradeStatisticsEntry>();
}
