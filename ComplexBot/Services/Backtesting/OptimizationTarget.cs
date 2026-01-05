namespace ComplexBot.Services.Backtesting;

public enum OptimizationTarget
{
    SharpeRatio,
    SortinoRatio,
    ProfitFactor,
    TotalReturn,
    RiskAdjusted  // Return / MaxDD * Sharpe
}
