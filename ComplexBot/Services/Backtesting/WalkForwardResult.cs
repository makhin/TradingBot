using System.Collections.Generic;

namespace ComplexBot.Services.Backtesting;

public record WalkForwardResult(
    decimal WalkForwardEfficiency,
    decimal AverageOosReturn,
    decimal AverageOosSharpe,
    decimal AverageOosMaxDrawdown,
    decimal OosConsistency,
    List<WalkForwardPeriod> Periods,
    bool IsRobust
);
