using ComplexBot.Models.Enums;

namespace ComplexBot.Models.Records;

public record TradeSignal(
    string Symbol,
    SignalType Type,
    decimal Price,
    decimal? StopLoss,
    decimal? TakeProfit,
    string Reason,
    decimal? PartialExitPercent = null,
    decimal? PartialExitQuantity = null,
    bool MoveStopToBreakeven = false
);
