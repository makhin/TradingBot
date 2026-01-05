namespace ComplexBot.Configuration.Trading;

/// <summary>
/// How total capital is allocated across trading pairs.
/// </summary>
public enum CapitalAllocationMode
{
    /// <summary>
    /// Equal allocation: divide capital equally among all enabled pairs.
    /// </summary>
    Equal,

    /// <summary>
    /// Weighted allocation: use WeightPercent from each TradingPairConfig.
    /// Weights should sum to approximately 100%.
    /// </summary>
    Weighted,

    /// <summary>
    /// Dynamic allocation: adjust based on volatility or other rules.
    /// </summary>
    Dynamic
}
