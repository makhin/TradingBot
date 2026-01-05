using ComplexBot.Models;

namespace ComplexBot.Services.Strategies;

public record StrategyVote
{
    public string StrategyName { get; init; } = "";
    public SignalType Signal { get; init; }
    public decimal Confidence { get; init; }
    public decimal Weight { get; init; }
    public decimal? StopLoss { get; init; }
    public decimal? TakeProfit { get; init; }
    public string Reason { get; init; } = "";
    public decimal? PartialExitPercent { get; init; }
    public bool MoveStopToBreakeven { get; init; }
}
