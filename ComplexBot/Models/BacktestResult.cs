using System;
using System.Collections.Generic;

namespace ComplexBot.Models;

public record BacktestResult(
    string StrategyName,
    DateTime StartDate,
    DateTime EndDate,
    decimal InitialCapital,
    decimal FinalCapital,
    List<Trade> Trades,
    List<decimal> EquityCurve,
    PerformanceMetrics Metrics
);
