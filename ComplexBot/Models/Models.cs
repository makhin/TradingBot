namespace ComplexBot.Models;

public record Candle(
    DateTime OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    DateTime CloseTime
);

public record Trade(
    string Symbol,
    DateTime EntryTime,
    DateTime? ExitTime,
    decimal EntryPrice,
    decimal? ExitPrice,
    decimal Quantity,
    TradeDirection Direction,
    decimal? StopLoss,
    decimal? TakeProfit,
    string? ExitReason
)
{
    public decimal? PnL => ExitPrice.HasValue 
        ? Direction == TradeDirection.Long 
            ? (ExitPrice.Value - EntryPrice) * Quantity
            : (EntryPrice - ExitPrice.Value) * Quantity
        : null;

    public decimal? PnLPercent => ExitPrice.HasValue
        ? Direction == TradeDirection.Long
            ? (ExitPrice.Value - EntryPrice) / EntryPrice * 100
            : (EntryPrice - ExitPrice.Value) / EntryPrice * 100
        : null;
}

public enum TradeDirection { Long, Short }

public record TradeSignal(
    string Symbol,
    SignalType Type,
    decimal Price,
    decimal? StopLoss,
    decimal? TakeProfit,
    string Reason
);

public enum SignalType { None, Buy, Sell, Exit }

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

public record PositionSizeResult(
    decimal Quantity,
    decimal RiskAmount,
    decimal StopDistance
);
