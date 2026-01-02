namespace ComplexBot.Services.Indicators;

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

/// <summary>
/// Generic indicator interface with typed input
/// </summary>
/// <typeparam name="TInput">Type of input data (e.g., decimal for price, Candle for OHLCV)</typeparam>
public interface IIndicator<in TInput> : IIndicator
{
    /// <summary>
    /// Updates the indicator with new data
    /// </summary>
    /// <param name="input">New data point</param>
    /// <returns>Current value if ready, null otherwise</returns>
    decimal? Update(TInput input);
}

/// <summary>
/// Interface for indicators that produce multiple output values
/// </summary>
public interface IMultiValueIndicator : IIndicator
{
    /// <summary>
    /// Gets all output values as a dictionary
    /// </summary>
    IReadOnlyDictionary<string, decimal?> Values { get; }
}
