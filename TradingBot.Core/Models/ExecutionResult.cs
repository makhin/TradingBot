namespace TradingBot.Core.Models;

/// <summary>
/// Exchange-agnostic result of order execution
/// </summary>
public record ExecutionResult
{
    public required bool Success { get; init; }
    public long OrderId { get; init; }
    public decimal FilledQuantity { get; init; }
    public decimal AveragePrice { get; init; }
    public string? ErrorMessage { get; init; }
}
