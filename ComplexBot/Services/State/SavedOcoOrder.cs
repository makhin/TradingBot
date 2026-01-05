namespace ComplexBot.Services.State;

public record SavedOcoOrder
{
    public string Symbol { get; init; } = "";
    public long OrderListId { get; init; }
}
