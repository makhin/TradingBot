namespace TradingBot.Indicators;

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
