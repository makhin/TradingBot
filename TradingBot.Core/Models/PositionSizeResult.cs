namespace TradingBot.Core.Models;

public record PositionSizeResult(
    decimal Quantity,
    decimal RiskAmount,
    decimal StopDistance
);
