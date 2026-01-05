using System.Collections.Generic;

namespace ComplexBot.Services.Strategies;

public record EnsembleStats
{
    public int StrategyCount { get; init; }
    public List<StrategyInfo> Strategies { get; init; } = new();
    public List<StrategyVote> LastVotes { get; init; } = new();
    public decimal MinimumAgreement { get; init; }
    public bool UseConfidenceWeighting { get; init; }
}
