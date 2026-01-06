using ComplexBot.Models;
using ComplexBot.Services.Analytics;
using System.IO;

namespace ComplexBot.Tests;

public class TradeJournalTests : IDisposable
{
    private readonly string _testOutputPath;
    private readonly TradeJournal _journal;

    public TradeJournalTests()
    {
        _testOutputPath = Path.Combine(Path.GetTempPath(), $"test_trades_{Guid.NewGuid()}");
        _journal = new TradeJournal(_testOutputPath);
    }

    private static IndicatorSnapshot CreateIndicators(
        decimal adx,
        decimal plusDi,
        decimal minusDi,
        decimal fastEma,
        decimal slowEma,
        decimal atr,
        decimal macdHistogram,
        decimal volumeRatio,
        decimal obvSlope)
    {
        return IndicatorSnapshot.FromPairs(
            ("ADX", adx),
            ("+DI", plusDi),
            ("-DI", minusDi),
            ("FastEMA", fastEma),
            ("SlowEMA", slowEma),
            ("ATR", atr),
            ("MACD_Hist", macdHistogram),
            ("VolumeRatio", volumeRatio),
            ("OBV_Slope", obvSlope)
        );
    }

    public void Dispose()
    {
        // Clean up test files
        if (Directory.Exists(_testOutputPath))
        {
            Directory.Delete(_testOutputPath, recursive: true);
        }
    }

    [Fact]
    public void OpenTrade_ReturnsUniqueTradeId()
    {
        // Arrange
        var entry = new TradeJournalEntry
        {
            EntryTime = DateTime.UtcNow,
            Symbol = "BTCUSDT",
            Direction = SignalType.Buy,
            EntryPrice = 45000m,
            StopLoss = 43500m,
            TakeProfit = 47250m,
            Quantity = 0.1m,
            PositionValueUsd = 4500m,
            RiskAmount = 150m,
            Indicators = CreateIndicators(28.5m, 25m, 15m, 45000m, 44500m, 300m, 100m, 1.8m, 50m),
            EntryReason = "ADX>25, EMA cross, Volume spike"
        };

        // Act
        int tradeId1 = _journal.OpenTrade(entry);
        int tradeId2 = _journal.OpenTrade(entry);

        // Assert
        Assert.NotEqual(0, tradeId1);
        Assert.NotEqual(0, tradeId2);
        Assert.NotEqual(tradeId1, tradeId2);
    }

    [Fact]
    public void CloseTrade_UpdatesExistingTrade()
    {
        // Arrange
        var openEntry = new TradeJournalEntry
        {
            EntryTime = DateTime.UtcNow,
            Symbol = "BTCUSDT",
            Direction = SignalType.Buy,
            EntryPrice = 45000m,
            StopLoss = 43500m,
            TakeProfit = 47250m,
            Quantity = 0.1m,
            PositionValueUsd = 4500m,
            RiskAmount = 150m,
            Indicators = CreateIndicators(28.5m, 25m, 15m, 45000m, 44500m, 300m, 100m, 1.8m, 50m),
            EntryReason = "ADX>25"
        };

        int tradeId = _journal.OpenTrade(openEntry);

        var closeEntry = new TradeJournalEntry
        {
            ExitTime = DateTime.UtcNow.AddHours(2),
            ExitPrice = 46500m,
            GrossPnL = 150m,
            NetPnL = 141m,
            RMultiple = 1.0m,
            Result = TradeResult.Win,
            ExitReason = "Take profit hit",
            BarsInTrade = 8,
            MaxAdverseExcursion = -75m,
            MaxFavorableExcursion = 200m
        };

        // Act
        _journal.CloseTrade(tradeId, closeEntry);
        var trades = _journal.GetAllTrades();

        // Assert
        var closedTrade = trades.First(t => t.TradeId == tradeId);
        Assert.Equal(46500m, closedTrade.ExitPrice);
        Assert.Equal(TradeResult.Win, closedTrade.Result);
        Assert.Equal("Take profit hit", closedTrade.ExitReason);
    }

    [Fact]
    public void GetStats_WithWinningTrades_CalculatesWinRate()
    {
        // Arrange
        var openEntry = new TradeJournalEntry
        {
            EntryTime = DateTime.UtcNow,
            Symbol = "BTCUSDT",
            Direction = SignalType.Buy,
            EntryPrice = 45000m,
            StopLoss = 43500m,
            TakeProfit = 47250m,
            Quantity = 0.1m,
            PositionValueUsd = 4500m,
            RiskAmount = 150m,
            Indicators = CreateIndicators(28.5m, 25m, 15m, 45000m, 44500m, 300m, 100m, 1.8m, 50m),
            EntryReason = "ADX>25"
        };

        int tradeId = _journal.OpenTrade(openEntry);

        var closeEntry = new TradeJournalEntry
        {
            ExitTime = DateTime.UtcNow.AddHours(2),
            ExitPrice = 46500m,
            GrossPnL = 150m,
            NetPnL = 141m,
            RMultiple = 1.0m,
            Result = TradeResult.Win,
            ExitReason = "Take profit hit",
            BarsInTrade = 8
        };

        _journal.CloseTrade(tradeId, closeEntry);

        // Act
        var stats = _journal.GetStats();

        // Assert
        Assert.Equal(1, stats.TotalTrades);
        Assert.Equal(100m, stats.WinRate);
        Assert.Equal(141m, stats.TotalNetPnL);
        Assert.Equal(141m, stats.AverageWin);
    }

    [Fact]
    public void GetStats_WithLosingAndWinningTrades_CalculatesCorrectMetrics()
    {
        // Arrange - Create 1 winning and 1 losing trade
        // Trade 1: Win
        var winEntry = new TradeJournalEntry
        {
            EntryTime = DateTime.UtcNow,
            Symbol = "BTCUSDT",
            Direction = SignalType.Buy,
            EntryPrice = 45000m,
            StopLoss = 43500m,
            TakeProfit = 47250m,
            Quantity = 0.1m,
            PositionValueUsd = 4500m,
            RiskAmount = 150m,
            Indicators = CreateIndicators(28.5m, 25m, 15m, 45000m, 44500m, 300m, 100m, 1.8m, 50m),
            EntryReason = "ADX>25"
        };

        int winId = _journal.OpenTrade(winEntry);
        _journal.CloseTrade(winId, new TradeJournalEntry
        {
            ExitTime = DateTime.UtcNow.AddHours(2),
            ExitPrice = 46500m,
            GrossPnL = 150m,
            NetPnL = 141m,
            RMultiple = 1.0m,
            Result = TradeResult.Win,
            ExitReason = "Take profit",
            BarsInTrade = 8
        });

        // Trade 2: Loss
        var lossEntry = new TradeJournalEntry
        {
            EntryTime = DateTime.UtcNow.AddHours(3),
            Symbol = "ETHUSDT",
            Direction = SignalType.Buy,
            EntryPrice = 3000m,
            StopLoss = 2850m,
            TakeProfit = 3150m,
            Quantity = 1m,
            PositionValueUsd = 3000m,
            RiskAmount = 150m,
            Indicators = CreateIndicators(20m, 18m, 12m, 3000m, 2950m, 50m, 20m, 1.2m, 30m),
            EntryReason = "ADX>20"
        };

        int lossId = _journal.OpenTrade(lossEntry);
        _journal.CloseTrade(lossId, new TradeJournalEntry
        {
            ExitTime = DateTime.UtcNow.AddHours(4),
            ExitPrice = 2850m,
            GrossPnL = -150m,
            NetPnL = -142m,
            RMultiple = -1.0m,
            Result = TradeResult.Loss,
            ExitReason = "Stop loss hit",
            BarsInTrade = 2
        });

        // Act
        var stats = _journal.GetStats();

        // Assert
        Assert.Equal(2, stats.TotalTrades);
        Assert.Equal(50m, stats.WinRate);  // 50% win rate
        Assert.True(stats.TotalNetPnL < 0);  // Overall loss due to worse loss
    }

    [Fact]
    public void ExportToCsv_CreatesValidFile()
    {
        // Arrange
        var entry = new TradeJournalEntry
        {
            EntryTime = DateTime.UtcNow,
            Symbol = "BTCUSDT",
            Direction = SignalType.Buy,
            EntryPrice = 45000m,
            StopLoss = 43500m,
            TakeProfit = 47250m,
            Quantity = 0.1m,
            PositionValueUsd = 4500m,
            RiskAmount = 150m,
            Indicators = CreateIndicators(28.5m, 25m, 15m, 45000m, 44500m, 300m, 100m, 1.8m, 50m),
            EntryReason = "ADX>25"
        };

        int tradeId = _journal.OpenTrade(entry);

        // Act
        _journal.ExportToCsv("test_export.csv");

        // Assert
        var csvPath = Path.Combine(_testOutputPath, "test_export.csv");
        Assert.True(File.Exists(csvPath));

        var lines = File.ReadAllLines(csvPath);
        Assert.True(lines.Length >= 2);  // Header + at least 1 trade
        Assert.Contains("BTCUSDT", lines[1]);
    }

    [Fact]
    public void ExportToCsv_WithTimestamp_CreatesNamedFile()
    {
        // Arrange
        var entry = new TradeJournalEntry
        {
            EntryTime = DateTime.UtcNow,
            Symbol = "BTCUSDT",
            Direction = SignalType.Buy,
            EntryPrice = 45000m,
            StopLoss = 43500m,
            TakeProfit = 47250m,
            Quantity = 0.1m,
            PositionValueUsd = 4500m,
            RiskAmount = 150m,
            Indicators = CreateIndicators(28.5m, 25m, 15m, 45000m, 44500m, 300m, 100m, 1.8m, 50m),
            EntryReason = "ADX>25"
        };

        _journal.OpenTrade(entry);

        // Act
        _journal.ExportToCsv();  // Default naming with timestamp

        // Assert
        var files = Directory.GetFiles(_testOutputPath, "trades_*.csv");
        Assert.True(files.Length > 0);
    }

    [Fact]
    public void GetAllTrades_ReturnsAllOpenedTrades()
    {
        // Arrange
        var entries = new[]
        {
            new TradeJournalEntry
            {
                EntryTime = DateTime.UtcNow,
                Symbol = "BTCUSDT",
                Direction = SignalType.Buy,
                EntryPrice = 45000m,
                Quantity = 0.1m,
                PositionValueUsd = 4500m,
                RiskAmount = 150m,
                Indicators = CreateIndicators(28.5m, 25m, 15m, 45000m, 44500m, 300m, 100m, 1.8m, 50m)
            },
            new TradeJournalEntry
            {
                EntryTime = DateTime.UtcNow.AddHours(1),
                Symbol = "ETHUSDT",
                Direction = SignalType.Sell,
                EntryPrice = 3000m,
                Quantity = 1m,
                PositionValueUsd = 3000m,
                RiskAmount = 150m,
                Indicators = CreateIndicators(20m, 18m, 12m, 3000m, 2950m, 50m, 20m, 1.2m, 30m)
            }
        };

        // Act
        int id1 = _journal.OpenTrade(entries[0]);
        int id2 = _journal.OpenTrade(entries[1]);

        var allTrades = _journal.GetAllTrades();

        // Assert
        Assert.Equal(2, allTrades.Count);
        Assert.Contains(allTrades, t => t.TradeId == id1 && t.Symbol == "BTCUSDT");
        Assert.Contains(allTrades, t => t.TradeId == id2 && t.Symbol == "ETHUSDT");
    }

    [Fact]
    public void GetStats_WithNoClosedTrades_ReturnsZeroMetrics()
    {
        // Arrange
        var entry = new TradeJournalEntry
        {
            EntryTime = DateTime.UtcNow,
            Symbol = "BTCUSDT",
            Direction = SignalType.Buy,
            EntryPrice = 45000m,
            Quantity = 0.1m,
            PositionValueUsd = 4500m,
            RiskAmount = 150m,
            Indicators = CreateIndicators(28.5m, 25m, 15m, 45000m, 44500m, 300m, 100m, 1.8m, 50m)
        };

        _journal.OpenTrade(entry);  // Don't close it

        // Act
        var stats = _journal.GetStats();

        // Assert
        Assert.Equal(0, stats.TotalTrades);
        Assert.Equal(0m, stats.WinRate);
        Assert.Equal(0m, stats.TotalNetPnL);
    }
}
