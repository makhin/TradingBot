namespace SignalBot.Models;

/// <summary>
/// Take profit target level
/// </summary>
public record TargetLevel
{
    public required int Index { get; init; }
    public required decimal Price { get; init; }
    public required decimal PercentToClose { get; init; }
    public required decimal QuantityToClose { get; init; }

    public bool IsHit { get; init; }
    public DateTime? HitAt { get; init; }
    public decimal? ActualClosePrice { get; init; }
    public long? OrderId { get; init; }

    // Action after reaching this target
    public decimal? MoveStopLossTo { get; init; }
}
