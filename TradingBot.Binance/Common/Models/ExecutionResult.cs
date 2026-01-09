namespace TradingBot.Binance.Common.Models;

/// <summary>
/// Result of order execution with slippage validation
/// </summary>
public record ExecutionResult
{
    public bool IsAcceptable { get; init; }
    public decimal ExpectedPrice { get; init; }
    public decimal ActualPrice { get; init; }
    public decimal SlippagePercent { get; init; }
    public decimal SlippageAmount { get; init; }
    public string? RejectReason { get; init; }
}
