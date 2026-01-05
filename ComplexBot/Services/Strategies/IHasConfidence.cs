namespace ComplexBot.Services.Strategies;

/// <summary>
/// Interface for strategies that provide confidence level for their signals.
/// Used by StrategyEnsemble for weighted voting.
/// </summary>
public interface IHasConfidence
{
    /// <summary>
    /// Returns confidence level for current signal (0.0-1.0).
    /// Higher values indicate stronger conviction in the signal.
    /// </summary>
    decimal GetConfidence();
}
