namespace SignalBot.Models;

/// <summary>
/// Bot state for persistence
/// </summary>
public record SignalBotState
{
    public DateTime LastUpdate { get; init; } = DateTime.UtcNow;
    public string Version { get; init; } = "1.0";

    // Open positions
    public IReadOnlyList<SignalPosition> OpenPositions { get; init; } = Array.Empty<SignalPosition>();

    // Pending signals (not yet opened)
    public IReadOnlyList<TradingSignal> PendingSignals { get; init; } = Array.Empty<TradingSignal>();

    // Session statistics
    public decimal SessionStartEquity { get; init; }
    public decimal CurrentEquity { get; init; }
    public int TotalSignalsReceived { get; init; }
    public int TotalPositionsOpened { get; init; }
    public int TotalPositionsClosed { get; init; }

    // Last processed message ID per channel (for deduplication)
    public Dictionary<long, int> LastProcessedMessageIds { get; init; } = new();
}
