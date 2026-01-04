namespace ComplexBot.Models.Records;

public record PerformanceMetrics(
    decimal TotalReturn,
    decimal AnnualizedReturn,
    decimal MaxDrawdown,
    decimal MaxDrawdownPercent,
    decimal SharpeRatio,
    decimal SortinoRatio,
    decimal ProfitFactor,
    decimal WinRate,
    int TotalTrades,
    int WinningTrades,
    int LosingTrades,
    decimal AverageWin,
    decimal AverageLoss,
    decimal LargestWin,
    decimal LargestLoss,
    TimeSpan AverageHoldingPeriod
);
