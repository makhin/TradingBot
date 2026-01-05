namespace ComplexBot.Models;

public record PositionSizeResult(
    decimal Quantity,
    decimal RiskAmount,
    decimal StopDistance
);
