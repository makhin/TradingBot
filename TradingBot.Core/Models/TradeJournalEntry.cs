using System;

namespace TradingBot.Core.Models;

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
    public IndicatorSnapshot Indicators { get; init; } = IndicatorSnapshot.Empty;

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
