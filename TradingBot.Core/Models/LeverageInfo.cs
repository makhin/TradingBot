namespace TradingBot.Core.Models;

/// <summary>
/// Leverage information for a Futures symbol
/// </summary>
public record LeverageInfo
{
    public required string Symbol { get; init; }
    public required int CurrentLeverage { get; init; }
    public required int MaxLeverage { get; init; }
    public required decimal MaxNotionalValue { get; init; }
}
