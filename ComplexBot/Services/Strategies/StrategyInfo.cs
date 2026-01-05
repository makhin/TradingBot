namespace ComplexBot.Services.Strategies;

public record StrategyInfo
{
    public string Name { get; init; } = "";
    public decimal Weight { get; init; }
}
