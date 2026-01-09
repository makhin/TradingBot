namespace TradingBot.Core.State;

public record SavedOcoOrder
{
    public string Symbol { get; init; } = "";
    public long OrderListId { get; init; }
}
