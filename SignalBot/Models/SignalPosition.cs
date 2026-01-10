namespace SignalBot.Models;

/// <summary>
/// Position opened from a trading signal
/// </summary>
public record SignalPosition
{
    // Identification
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid SignalId { get; init; }

    // Position parameters
    public required string Symbol { get; init; }
    public required SignalDirection Direction { get; init; }
    public required PositionStatus Status { get; init; }

    // Prices
    public required decimal PlannedEntryPrice { get; init; }
    public decimal ActualEntryPrice { get; init; }
    public required decimal CurrentStopLoss { get; init; }
    public required int Leverage { get; init; }

    // Quantity
    public decimal InitialQuantity { get; init; }
    public decimal RemainingQuantity { get; init; }
    public decimal FilledQuantity => InitialQuantity - RemainingQuantity;

    // Targets
    public required IReadOnlyList<TargetLevel> Targets { get; init; }
    public int TargetsHit => Targets.Count(t => t.IsHit);

    // Exchange orders
    public long? EntryOrderId { get; init; }
    public long? StopLossOrderId { get; init; }
    public IReadOnlyList<long> TakeProfitOrderIds { get; init; } = Array.Empty<long>();

    // Timestamps
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? OpenedAt { get; init; }
    public DateTime? ClosedAt { get; init; }

    // P&L
    public decimal RealizedPnl { get; init; }
    public decimal UnrealizedPnl { get; init; }
    public decimal Commission { get; init; }

    // Close reason
    public PositionCloseReason? CloseReason { get; init; }
}
