using ComplexBot.Models;

namespace ComplexBot.Services.Backtesting;

public record MultiTimeframeBacktestResult(
    BacktestResult Result,
    int TotalSignals,
    int ApprovedSignals,
    int BlockedSignals
)
{
    public decimal ApprovalRate => TotalSignals > 0
        ? (decimal)ApprovedSignals / TotalSignals * 100m
        : 0m;
}
