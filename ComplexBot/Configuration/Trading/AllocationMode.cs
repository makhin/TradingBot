namespace ComplexBot.Configuration.Trading;

/// <summary>
/// How total capital is allocated across trading pairs.
/// </summary>
public enum AllocationMode
{
    /// <summary>
    /// Equal allocation: divide capital equally among all enabled pairs.
    /// </summary>
    Equal,

    /// <summary>
    /// Weighted allocation: use WeightPercent from each TradingPairConfig.
    /// Weights must sum to approximately 100%.
    /// </summary>
    Weighted,

    /// <summary>
    /// Kelly criterion: allocate based on strategy win rate and R:R ratio.
    /// Requires backtest statistics.
    /// </summary>
    Kelly
}
