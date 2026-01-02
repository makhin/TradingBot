using ComplexBot.Models;
using System.Globalization;

namespace ComplexBot.Services.Analytics;

public class TradeJournal
{
    private readonly List<TradeJournalEntry> _entries = new();
    private readonly string _outputPath;
    private int _nextTradeId = 1;

    public TradeJournal(string outputPath = "trades")
    {
        _outputPath = outputPath;
        Directory.CreateDirectory(_outputPath);
    }

    public int OpenTrade(TradeJournalEntry entry)
    {
        var tradeId = _nextTradeId++;
        _entries.Add(entry with { TradeId = tradeId });
        return tradeId;
    }

    public void CloseTrade(int tradeId, TradeJournalEntry updates)
    {
        var index = _entries.FindIndex(e => e.TradeId == tradeId);
        if (index >= 0)
        {
            _entries[index] = _entries[index] with
            {
                ExitTime = updates.ExitTime,
                ExitPrice = updates.ExitPrice,
                GrossPnL = updates.GrossPnL,
                NetPnL = updates.NetPnL,
                RMultiple = updates.RMultiple,
                Result = updates.Result,
                ExitReason = updates.ExitReason,
                BarsInTrade = updates.BarsInTrade,
                Duration = updates.Duration,
                MaxAdverseExcursion = updates.MaxAdverseExcursion,
                MaxFavorableExcursion = updates.MaxFavorableExcursion
            };
        }
    }

    public void ExportToCsv(string? filename = null)
    {
        filename ??= $"trades_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
        var path = Path.Combine(_outputPath, filename);

        using var writer = new StreamWriter(path);

        // Header
        writer.WriteLine("TradeId,EntryTime,ExitTime,Symbol,Direction," +
            "EntryPrice,ExitPrice,StopLoss,TakeProfit," +
            "Quantity,PositionValue,RiskAmount," +
            "GrossPnL,NetPnL,RMultiple,Result," +
            "ADX,+DI,-DI,FastEMA,SlowEMA,ATR,MACD_Hist,VolumeRatio,OBV_Slope," +
            "EntryReason,ExitReason,BarsInTrade,Duration,MAE,MFE");

        foreach (var e in _entries)
        {
            writer.WriteLine($"{e.TradeId},{FormatDateTime(e.EntryTime)},{FormatDateTime(e.ExitTime)},{e.Symbol},{e.Direction}," +
                $"{e.EntryPrice},{FormatDecimal(e.ExitPrice)},{e.StopLoss},{e.TakeProfit}," +
                $"{e.Quantity},{e.PositionValueUsd},{e.RiskAmount}," +
                $"{FormatDecimal(e.GrossPnL)},{FormatDecimal(e.NetPnL)},{FormatDecimal(e.RMultiple)},{e.Result}," +
                $"{e.AdxValue},{e.PlusDi},{e.MinusDi},{e.FastEma},{e.SlowEma},{e.Atr},{e.MacdHistogram},{e.VolumeRatio},{e.ObvSlope}," +
                $"\"{e.EntryReason}\",\"{e.ExitReason}\",{e.BarsInTrade},{FormatDuration(e.Duration)},{FormatDecimal(e.MaxAdverseExcursion)},{FormatDecimal(e.MaxFavorableExcursion)}");
        }

        Console.WriteLine($"📊 Trade journal exported: {path}");
    }

    public TradeJournalStats GetStats()
    {
        var closed = _entries.Where(e => e.ExitTime.HasValue).ToList();

        if (closed.Count == 0)
        {
            return new TradeJournalStats
            {
                TotalTrades = 0,
                WinRate = 0,
                AverageRMultiple = 0,
                TotalNetPnL = 0,
                AverageWin = 0,
                AverageLoss = 0,
                LargestWin = 0,
                LargestLoss = 0,
                AverageBarsInTrade = 0
            };
        }

        var wins = closed.Where(e => e.Result == TradeResult.Win).ToList();
        var losses = closed.Where(e => e.Result == TradeResult.Loss).ToList();

        return new TradeJournalStats
        {
            TotalTrades = closed.Count,
            WinRate = (decimal)wins.Count / closed.Count * 100,
            AverageRMultiple = closed.Average(e => e.RMultiple ?? 0),
            TotalNetPnL = closed.Sum(e => e.NetPnL ?? 0),
            AverageWin = wins.Any() ? wins.Average(e => e.NetPnL ?? 0) : 0,
            AverageLoss = losses.Any() ? losses.Average(e => e.NetPnL ?? 0) : 0,
            LargestWin = closed.Max(e => e.NetPnL ?? 0),
            LargestLoss = closed.Min(e => e.NetPnL ?? 0),
            AverageBarsInTrade = closed.Average(e => e.BarsInTrade)
        };
    }

    public IReadOnlyList<TradeJournalEntry> GetAllTrades() => _entries.AsReadOnly();

    private static string FormatDateTime(DateTime? dt)
        => dt?.ToString("O") ?? "";

    private static string FormatDecimal(decimal? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static string FormatDuration(TimeSpan? duration)
        => duration.HasValue ? $"{duration.Value.TotalHours:F1}h" : "";
}
