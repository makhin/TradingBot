using TradingBot.Core.Models;

namespace TradingBot.Core.RiskManagement;

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
