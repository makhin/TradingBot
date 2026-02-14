namespace TradingBot.Bitget.Futures.Models;

/// <summary>
/// Bitget order execution result
/// </summary>
public class ExecutionResult
{
    public bool IsAcceptable { get; set; }
    public string? RejectReason { get; set; }
    public long OrderId { get; set; }
    public decimal FilledQuantity { get; set; }
    public decimal ExpectedPrice { get; set; }
    public decimal ActualPrice { get; set; }
    public decimal SlippagePercent { get; set; }
    public decimal SlippageAmount { get; set; }
}
