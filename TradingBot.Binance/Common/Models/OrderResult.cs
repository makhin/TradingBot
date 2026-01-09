namespace TradingBot.Binance.Common.Models;

/// <summary>
/// Result of a single order execution
/// </summary>
public record OrderResult(
    bool Success,
    decimal FilledQuantity,
    decimal AveragePrice,
    string? ErrorMessage);
