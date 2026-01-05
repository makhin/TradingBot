using System;
using System.Collections.Generic;
using System.Linq;
using ComplexBot.Services.Strategies;

namespace ComplexBot.Services.Backtesting;

public record OptimizationResult(
    List<ParameterSetResult> TopResults,
    List<ParameterSetResult> RobustResults,
    int TotalCombinations,
    int ValidCombinations,
    DateTime InSampleStart,
    DateTime InSampleEnd,
    DateTime OutOfSampleStart,
    DateTime OutOfSampleEnd
)
{
    public StrategySettings? BestRobustParameters => RobustResults.FirstOrDefault()?.Parameters;
    public StrategySettings? BestInSampleParameters => TopResults.FirstOrDefault()?.Parameters;
}
