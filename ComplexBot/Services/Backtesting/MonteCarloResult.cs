using System.Collections.Generic;

namespace ComplexBot.Services.Backtesting;

public record MonteCarloResult(
    decimal OriginalReturn,
    decimal MedianReturn,
    decimal Percentile5Return,
    decimal Percentile95Return,
    decimal AverageMaxDrawdown,
    decimal Percentile5Drawdown,
    decimal Percentile95Drawdown,
    decimal RuinProbability,
    List<decimal> AllReturns,
    List<decimal> AllDrawdowns
)
{
    public bool IsWithinConfidenceBand(decimal liveReturn) =>
        liveReturn >= Percentile5Return && liveReturn <= Percentile95Return;

    public string GetConfidenceAssessment() =>
        RuinProbability switch
        {
            < 1 => "Excellent - Very low risk of significant loss",
            < 5 => "Good - Acceptable risk level",
            < 10 => "Moderate - Consider reducing position sizes",
            _ => "High Risk - Strategy needs review"
        };
}
