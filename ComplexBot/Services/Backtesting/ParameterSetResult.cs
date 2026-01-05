using ComplexBot.Models;
using ComplexBot.Services.Strategies;

namespace ComplexBot.Services.Backtesting;

public record ParameterSetResult(
    StrategySettings Parameters,
    BacktestResult InSampleResult,
    BacktestResult OutOfSampleResult,
    decimal InSampleScore,
    decimal OutOfSampleScore
)
{
    public decimal Robustness => InSampleScore > 0 ? OutOfSampleScore / InSampleScore * 100 : 0;
    public bool IsRobust => Robustness >= 50; // OOS >= 50% of IS
}
