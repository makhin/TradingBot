using TradingBot.Core.Models;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using Serilog;

namespace TradingBot.Core.Analytics;

public class TradeJournal : ITradeJournal
{
    private readonly List<TradeJournalEntry> _entries = new();
    private readonly string _outputPath;
    private readonly object _sync = new();
    private readonly ILogger _logger;
    private int _nextTradeId = 1;

    public TradeJournal(string outputPath = "trades", ILogger? logger = null)
    {
        _outputPath = outputPath;
        _logger = logger ?? Log.ForContext<TradeJournal>();
        Directory.CreateDirectory(_outputPath);
    }

    public int OpenTrade(TradeJournalEntry entry)
    {
        lock (_sync)
        {
            var tradeId = _nextTradeId++;
            _entries.Add(entry with { TradeId = tradeId });
            return tradeId;
        }
    }

    public void CloseTrade(int tradeId, TradeJournalEntry updates)
    {
        lock (_sync)
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
    }

    public void ExportToCsv(string? filename = null)
    {
        filename ??= $"trades_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
        var path = Path.Combine(_outputPath, filename);

        var entriesSnapshot = GetEntriesSnapshot();
        var indicatorKeys = entriesSnapshot
            .SelectMany(entry => entry.Indicators.Values.Keys)
            .Distinct()
            .OrderBy(k => k)
            .ToList();

        using var writer = new StreamWriter(path);
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

        var preIndicatorHeaders = new[]
        {
            "TradeId",
            "EntryTime",
            "ExitTime",
            "Symbol",
            "Direction",
            "EntryPrice",
            "ExitPrice",
            "StopLoss",
            "TakeProfit",
            "Quantity",
            "PositionValue",
            "RiskAmount",
            "GrossPnL",
            "NetPnL",
            "RMultiple",
            "Result"
        };

        var postIndicatorHeaders = new[]
        {
            "EntryReason",
            "ExitReason",
            "BarsInTrade",
            "Duration",
            "MAE",
            "MFE"
        };

        foreach (var header in preIndicatorHeaders)
        {
            csv.WriteField(header);
        }

        foreach (var indicatorKey in indicatorKeys)
        {
            csv.WriteField(indicatorKey);
        }

        foreach (var header in postIndicatorHeaders)
        {
            csv.WriteField(header);
        }

        csv.NextRecord();

        foreach (var e in entriesSnapshot)
        {
            csv.WriteField(e.TradeId);
            csv.WriteField(FormatDateTime(e.EntryTime));
            csv.WriteField(FormatDateTime(e.ExitTime));
            csv.WriteField(e.Symbol);
            csv.WriteField(e.Direction.ToString());
            csv.WriteField(FormatDecimal(e.EntryPrice));
            csv.WriteField(FormatDecimal(e.ExitPrice));
            csv.WriteField(FormatDecimal(e.StopLoss));
            csv.WriteField(FormatDecimal(e.TakeProfit));
            csv.WriteField(FormatDecimal(e.Quantity));
            csv.WriteField(FormatDecimal(e.PositionValueUsd));
            csv.WriteField(FormatDecimal(e.RiskAmount));
            csv.WriteField(FormatDecimal(e.GrossPnL));
            csv.WriteField(FormatDecimal(e.NetPnL));
            csv.WriteField(FormatDecimal(e.RMultiple));
            csv.WriteField(e.Result?.ToString() ?? "");

            foreach (var indicatorKey in indicatorKeys)
            {
                csv.WriteField(FormatDecimal(e.Indicators.GetValue(indicatorKey)));
            }

            csv.WriteField(e.EntryReason);
            csv.WriteField(e.ExitReason);
            csv.WriteField(e.BarsInTrade);
            csv.WriteField(FormatDuration(e.Duration));
            csv.WriteField(FormatDecimal(e.MaxAdverseExcursion));
            csv.WriteField(FormatDecimal(e.MaxFavorableExcursion));

            csv.NextRecord();
        }

        _logger.Information("📊 Trade journal exported: {Path}", path);
    }

    public TradeJournalStats GetStats()
    {
        var closed = GetEntriesSnapshot().Where(e => e.ExitTime.HasValue).ToList();

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

    public IReadOnlyList<TradeJournalEntry> GetAllTrades()
        => GetEntriesSnapshot().AsReadOnly();

    private List<TradeJournalEntry> GetEntriesSnapshot()
    {
        lock (_sync)
        {
            return _entries.ToList();
        }
    }

    private static string FormatDateTime(DateTime? dt)
        => dt?.ToString("O") ?? "";

    private static string FormatDecimal(decimal? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static string FormatDuration(TimeSpan? duration)
        => duration.HasValue ? $"{duration.Value.TotalHours:F1}h" : "";
}
