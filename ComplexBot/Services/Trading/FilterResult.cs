namespace ComplexBot.Services.Trading;

/// <summary>
/// Result of a filter evaluation.
/// </summary>
/// <param name="Approved">Whether the filter approves the signal (Confirm/Veto modes)</param>
/// <param name="Reason">Human-readable explanation of the decision</param>
/// <param name="ConfidenceAdjustment">Optional confidence multiplier for Score mode (0.0 - 1.0)</param>
public record FilterResult(
    bool Approved,
    string Reason,
    decimal? ConfidenceAdjustment = null
);
