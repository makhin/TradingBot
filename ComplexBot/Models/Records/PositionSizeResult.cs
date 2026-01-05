namespace ComplexBot.Models.Records;

public record PositionSizeResult(
    decimal Quantity,
    decimal RiskAmount,
    decimal StopDistance
);
