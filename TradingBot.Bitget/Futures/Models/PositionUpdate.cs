using TradingBot.Core.Models;

namespace TradingBot.Bitget.Futures.Models;

/// <summary>
/// Bitget position update structure
/// </summary>
public record PositionUpdate
{
    public required string Symbol { get; init; }
    public required PositionSide Side { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal EntryPrice { get; init; }
    public required decimal MarkPrice { get; init; }
    public required decimal UnrealizedPnl { get; init; }
    public required decimal LiquidationPrice { get; init; }
    public DateTime UpdateTime { get; init; }
}
