using TradingBot.Core.Models;

namespace TradingBot.Bitget.Futures.Models;

/// <summary>
/// Bitget order update structure
/// </summary>
public record OrderUpdate
{
    public required string Symbol { get; init; }
    public required long OrderId { get; init; }
    public required string ClientOrderId { get; init; }
    public required string Status { get; init; }
    public required TradeDirection Side { get; init; }
    public required decimal Price { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal FilledQuantity { get; init; }
    public required decimal AveragePrice { get; init; }
    public DateTime UpdateTime { get; init; }
}
