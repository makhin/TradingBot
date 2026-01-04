namespace ComplexBot.Models.Records;

public record Candle(
    DateTime OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    DateTime CloseTime
);
