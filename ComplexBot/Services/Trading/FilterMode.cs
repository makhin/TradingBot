namespace ComplexBot.Services.Trading;

/// <summary>
/// Defines how a filter affects signal decisions in multi-timeframe analysis.
/// </summary>
public enum FilterMode
{
    /// <summary>
    /// Signal must be explicitly confirmed by filter to proceed.
    /// If filter returns Approved=false, signal is rejected.
    /// </summary>
    Confirm,

    /// <summary>
    /// Filter can veto signals but doesn't need to confirm.
    /// Only if filter explicitly rejects (Approved=false) is signal blocked.
    /// </summary>
    Veto,

    /// <summary>
    /// Filter adjusts signal confidence/position sizing but doesn't block.
    /// Uses ConfidenceAdjustment (0.0 - 1.0) to scale position size.
    /// </summary>
    Score
}
