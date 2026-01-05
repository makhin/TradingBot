namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Fitness function types
/// </summary>
public enum FitnessFunction
{
    Sharpe,
    Sortino,
    ProfitFactor,
    Return,
    RiskAdjusted,
    Combined
}
