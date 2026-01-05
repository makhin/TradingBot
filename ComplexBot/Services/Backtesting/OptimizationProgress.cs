using ComplexBot.Services.Strategies;

namespace ComplexBot.Services.Backtesting;

public record OptimizationProgress(int Current, int Total, StrategySettings CurrentParameters)
{
    public int PercentComplete => Total > 0 ? Current * 100 / Total : 0;
}
