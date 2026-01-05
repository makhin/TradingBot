using ComplexBot.Models;
using ComplexBot.Services.Strategies;

namespace ComplexBot.Services.Trading;

/// <summary>
/// Interface for filtering trading signals based on additional criteria.
/// Used in multi-timeframe analysis where a filter strategy (e.g., RSI on 1h)
/// can confirm, veto, or score signals from a primary strategy (e.g., ADX on 4h).
/// </summary>
public interface ISignalFilter
{
    /// <summary>
    /// Name of the filter for logging and identification.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// How this filter affects signal decisions.
    /// </summary>
    FilterMode Mode { get; }

    /// <summary>
    /// Evaluates whether to approve a trading signal based on filter strategy state.
    /// </summary>
    /// <param name="signal">The signal from the primary strategy</param>
    /// <param name="filterState">Current state of the filter strategy</param>
    /// <returns>Result indicating approval status and reasoning</returns>
    FilterResult Evaluate(TradeSignal signal, StrategyState filterState);
}
