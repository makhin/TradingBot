using ComplexBot.Models;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.Trading;

namespace ComplexBot.Services.Backtesting;

public record MultiTimeframeFilterDefinition(
    string Name,
    IStrategy Strategy,
    ISignalFilter Filter,
    List<Candle> Candles
);
