namespace SignalBot.Models;

/// <summary>
/// Limit order pricing strategy
/// </summary>
public enum LimitOrderPricing
{
    /// <summary>
    /// Place limit order exactly at Entry price from signal
    /// </summary>
    AtEntry,

    /// <summary>
    /// Place limit order at current market price (aggressive)
    /// </summary>
    AtCurrentPrice,

    /// <summary>
    /// Place limit order between Entry and current price
    /// </summary>
    MidPoint
}
