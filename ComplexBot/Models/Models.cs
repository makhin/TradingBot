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
    string Reason,
    decimal? PartialExitPercent = null,
    decimal? PartialExitQuantity = null,
    bool MoveStopToBreakeven = false
);

public enum SignalType { None, Buy, Sell, Exit, PartialExit }

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

public record TradeJournalEntry
{
    public int TradeId { get; init; }
    public DateTime EntryTime { get; init; }
    public DateTime? ExitTime { get; init; }
    public string Symbol { get; init; } = "";
    public SignalType Direction { get; init; }  // Buy/Sell

    // Цены
    public decimal EntryPrice { get; init; }
    public decimal? ExitPrice { get; init; }
    public decimal StopLoss { get; init; }
    public decimal TakeProfit { get; init; }

    // Размер позиции
    public decimal Quantity { get; init; }
    public decimal PositionValueUsd { get; init; }
    public decimal RiskAmount { get; init; }

    // Результат
    public decimal? GrossPnL { get; init; }
    public decimal? NetPnL { get; init; }  // После комиссий
    public decimal? RMultiple { get; init; }  // PnL / RiskAmount
    public TradeResult? Result { get; init; }  // Win/Loss/Breakeven

    // Индикаторы на момент входа
    public decimal AdxValue { get; init; }
    public decimal PlusDi { get; init; }
    public decimal MinusDi { get; init; }
    public decimal FastEma { get; init; }
    public decimal SlowEma { get; init; }
    public decimal Atr { get; init; }
    public decimal MacdHistogram { get; init; }
    public decimal VolumeRatio { get; init; }  // CurrentVol / AvgVol
    public decimal ObvSlope { get; init; }

    // Причины входа/выхода
    public string EntryReason { get; init; } = "";
    public string ExitReason { get; init; } = "";

    // Время в сделке
    public int BarsInTrade { get; init; }
    public TimeSpan? Duration { get; init; }

    // MAE/MFE (Maximum Adverse/Favorable Excursion)
    public decimal? MaxAdverseExcursion { get; init; }  // Худшая точка
    public decimal? MaxFavorableExcursion { get; init; }  // Лучшая точка
}

public enum TradeResult { Win, Loss, Breakeven }

public record TradeJournalStats
{
    public int TotalTrades { get; init; }
    public decimal WinRate { get; init; }
    public decimal AverageRMultiple { get; init; }
    public decimal TotalNetPnL { get; init; }
    public decimal AverageWin { get; init; }
    public decimal AverageLoss { get; init; }
    public decimal LargestWin { get; init; }
    public decimal LargestLoss { get; init; }
    public double AverageBarsInTrade { get; init; }
}
