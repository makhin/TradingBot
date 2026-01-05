using ComplexBot.Models;

namespace ComplexBot.Services.RiskManagement;

public record OpenPosition(
    string Symbol,
    SignalType Direction,
    decimal Quantity,
    decimal RemainingQuantity,
    decimal RiskAmount,
    decimal EntryPrice,
    decimal StopLoss,
    bool BreakevenMoved,
    decimal CurrentPrice
);
