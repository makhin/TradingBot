using ComplexBot.Services.Trading;

namespace ComplexBot.Services.Backtesting;

public record MultiTimeframeOptimizationResult(
    string Configuration,
    string? FilterInterval,
    string? FilterStrategy,
    FilterMode? FilterMode,
    Dictionary<string, decimal> FilterParameters,
    MultiTimeframeBacktestResult Backtest,
    decimal Score,
    bool IsBaseline
);
