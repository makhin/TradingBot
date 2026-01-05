using System;
using ComplexBot.Models;

namespace ComplexBot.Services.Backtesting;

public record WalkForwardPeriod(
    DateTime InSampleStart,
    DateTime InSampleEnd,
    DateTime OutOfSampleStart,
    DateTime OutOfSampleEnd,
    BacktestResult InSampleResult,
    BacktestResult OutOfSampleResult
)
{
    public decimal WfeForPeriod => InSampleResult.Metrics.AnnualizedReturn != 0
        ? OutOfSampleResult.Metrics.AnnualizedReturn / InSampleResult.Metrics.AnnualizedReturn * 100
        : 0;
}
