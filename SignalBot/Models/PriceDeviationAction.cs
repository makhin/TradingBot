namespace SignalBot.Models;

/// <summary>
/// Action to take when price deviates from signal entry
/// </summary>
public enum PriceDeviationAction
{
    /// <summary>
    /// Skip the signal entirely
    /// </summary>
    Skip,

    /// <summary>
    /// Enter at market price despite deviation
    /// </summary>
    EnterAtMarket,

    /// <summary>
    /// Place limit order at Entry price from signal
    /// </summary>
    PlaceLimitAtEntry,

    /// <summary>
    /// Enter at market but adjust targets proportionally
    /// </summary>
    EnterAndAdjustTargets
}
