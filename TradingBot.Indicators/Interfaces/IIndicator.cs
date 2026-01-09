namespace TradingBot.Indicators;

/// <summary>
/// Base interface for all technical indicators
/// </summary>
public interface IIndicator
{
    /// <summary>
    /// Current calculated value of the indicator
    /// </summary>
    decimal? Value { get; }

    /// <summary>
    /// Whether the indicator has enough data to produce valid values
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Resets the indicator state
    /// </summary>
    void Reset();
}
