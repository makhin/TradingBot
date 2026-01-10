namespace SignalBot.Models;

/// <summary>
/// Trading signal received from Telegram channel
/// </summary>
public record TradingSignal
{
    // Metadata
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string RawText { get; init; }
    public DateTime ReceivedAt { get; init; } = DateTime.UtcNow;
    public required SignalSource Source { get; init; }

    // Parsed data from signal
    public required string Symbol { get; init; }
    public required SignalDirection Direction { get; init; }
    public required decimal Entry { get; init; }
    public required decimal OriginalStopLoss { get; init; }
    public required IReadOnlyList<decimal> Targets { get; init; }
    public required int OriginalLeverage { get; init; }

    // Calculated/adjusted values
    public decimal AdjustedStopLoss { get; init; }
    public int AdjustedLeverage { get; init; }
    public decimal LiquidationPrice { get; init; }
    public decimal RiskRewardRatio { get; init; }

    // Validation
    public bool IsValid { get; init; }
    public IReadOnlyList<string> ValidationWarnings { get; init; } = Array.Empty<string>();
}
