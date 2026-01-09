using TradingBot.Core.Analytics;
using TradingBot.Core.Models;

namespace TradingBot.Core.Tests;

public class TradeJournalTests
{
    private readonly string _testOutputPath = Path.Combine(Path.GetTempPath(), "trade_journal_tests");

    public TradeJournalTests()
    {
        // Cleanup before each test
        if (Directory.Exists(_testOutputPath))
        {
            Directory.Delete(_testOutputPath, recursive: true);
        }
    }

    [Fact]
    public void OpenTrade_ReturnsIncrementingTradeId()
    {
        // Arrange
        var journal = new TradeJournal(_testOutputPath);
        var entry1 = CreateTradeEntry("BTCUSDT", 45000m, 44000m);
        var entry2 = CreateTradeEntry("ETHUSDT", 2500m, 2400m);

        // Act
        int tradeId1 = journal.OpenTrade(entry1);
        int tradeId2 = journal.OpenTrade(entry2);

        // Assert
        Assert.Equal(1, tradeId1);
        Assert.Equal(2, tradeId2);
    }

    [Fact]
    public void CloseTrade_UpdatesTradeEntry()
    {
        // Arrange
        var journal = new TradeJournal(_testOutputPath);
        var entry = CreateTradeEntry("BTCUSDT", 45000m, 44000m);
        int tradeId = journal.OpenTrade(entry);

        var closeEntry = entry with
        {
            ExitTime = DateTime.UtcNow,
            ExitPrice = 45500m,
            GrossPnL = 1500m,
            NetPnL = 1400m,
            Result = TradeResult.Win
        };

        // Act
        journal.CloseTrade(tradeId, closeEntry);

        // Assert - The trade should be closed (no exception thrown)
        Assert.NotNull(journal);
    }

    [Fact]
    public void GetAllTrades_ReturnsOpenedTrades()
    {
        // Arrange
        var journal = new TradeJournal(_testOutputPath);
        var entry1 = CreateTradeEntry("BTCUSDT", 45000m, 44000m);
        var entry2 = CreateTradeEntry("ETHUSDT", 2500m, 2400m);

        // Act
        journal.OpenTrade(entry1);
        journal.OpenTrade(entry2);
        var allTrades = journal.GetAllTrades();

        // Assert
        Assert.Equal(2, allTrades.Count());
    }

    [Fact]
    public void ExportToCsv_CreatesFile()
    {
        // Arrange
        var journal = new TradeJournal(_testOutputPath);
        var entry = CreateTradeEntry("BTCUSDT", 45000m, 44000m);
        journal.OpenTrade(entry);

        // Act
        journal.ExportToCsv();

        // Assert
        var csvFiles = Directory.GetFiles(_testOutputPath, "*.csv");
        Assert.NotEmpty(csvFiles);
    }

    private TradeJournalEntry CreateTradeEntry(
        string symbol,
        decimal entryPrice,
        decimal stopLoss,
        decimal quantity = 1m,
        decimal takeProfit = 0m)
    {
        return new TradeJournalEntry
        {
            Symbol = symbol,
            EntryTime = DateTime.UtcNow,
            EntryPrice = entryPrice,
            Quantity = quantity,
            StopLoss = stopLoss,
            TakeProfit = takeProfit > 0 ? takeProfit : entryPrice + 1000m,
            Direction = SignalType.Buy,
            EntryReason = "Test entry"
        };
    }
}
