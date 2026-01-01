using System;

namespace SimpleBot.Models;

public record MarketData(
    string Symbol,
    decimal Price,
    DateTime Timestamp
);

public record TradeSignal(
    string Symbol,
    SignalType Type,
    decimal Price,
    decimal Quantity
);

public enum SignalType
{
    None,
    Buy,
    Sell
}