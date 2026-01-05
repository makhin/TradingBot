using ComplexBot.Models;
using ComplexBot.Models.Records;
using ComplexBot.Models.Enums;
using ComplexBot.Services.Trading;
using ComplexBot.Services.Trading.SignalFilters;
using Moq;

namespace ComplexBot.Tests;

public class SymbolTradingUnitTests
{
    #region Setup and Basic Tests

    [Fact]
    public void Constructor_InitializesWithPrimaryTrader()
    {
        // Arrange
        var mockTrader = new Mock<ISymbolTrader>();

        // Act
        var unit = new SymbolTradingUnit("BTCUSDT", mockTrader.Object);

        // Assert
        Assert.Equal("BTCUSDT", unit.Symbol);
        Assert.Equal(mockTrader.Object, unit.PrimaryTrader);
        Assert.Empty(unit.Filters);
    }

    [Fact]
    public void AddFilter_AddsFilterToCollection()
    {
        // Arrange
        var mockPrimaryTrader = new Mock<ISymbolTrader>();
        var mockFilterTrader = new Mock<ISymbolTrader>();
        mockFilterTrader.Setup(t => t.Symbol).Returns("BTCUSDT");

        var filter = new RsiSignalFilter(70m, 30m, FilterMode.Confirm);

        var unit = new SymbolTradingUnit("BTCUSDT", mockPrimaryTrader.Object);

        // Act
        unit.AddFilter(mockFilterTrader.Object, filter);

        // Assert
        Assert.Single(unit.Filters);
        Assert.Equal(filter, unit.Filters[0].Filter);
        Assert.Equal(mockFilterTrader.Object, unit.Filters[0].Trader);
    }

    [Fact]
    public void AddMultipleFilters_MaintainsOrder()
    {
        // Arrange
        var mockPrimaryTrader = new Mock<ISymbolTrader>();
        var mockRsiFilter = new Mock<ISymbolTrader>();
        mockRsiFilter.Setup(t => t.Symbol).Returns("BTCUSDT");

        var mockAdxFilter = new Mock<ISymbolTrader>();
        mockAdxFilter.Setup(t => t.Symbol).Returns("BTCUSDT");

        var mockTrendFilter = new Mock<ISymbolTrader>();
        mockTrendFilter.Setup(t => t.Symbol).Returns("BTCUSDT");

        var rsiFilter = new RsiSignalFilter(70m, 30m, FilterMode.Confirm);
        var adxFilter = new AdxSignalFilter(20m, 30m, FilterMode.Score);
        var trendFilter = new TrendAlignmentFilter(FilterMode.Veto, true);

        var unit = new SymbolTradingUnit("BTCUSDT", mockPrimaryTrader.Object);

        // Act
        unit.AddFilter(mockRsiFilter.Object, rsiFilter);
        unit.AddFilter(mockAdxFilter.Object, adxFilter);
        unit.AddFilter(mockTrendFilter.Object, trendFilter);

        // Assert
        Assert.Equal(3, unit.Filters.Count);
        Assert.Equal(rsiFilter, unit.Filters[0].Filter);
        Assert.Equal(adxFilter, unit.Filters[1].Filter);
        Assert.Equal(trendFilter, unit.Filters[2].Filter);
    }

    [Fact]
    public void StartAsync_StartsAllTraders()
    {
        // Arrange
        var mockPrimaryTrader = new Mock<ISymbolTrader>();
        var mockFilterTrader1 = new Mock<ISymbolTrader>();
        mockFilterTrader1.Setup(t => t.Symbol).Returns("BTCUSDT");
        mockFilterTrader1.Setup(t => t.StartAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mockFilterTrader2 = new Mock<ISymbolTrader>();
        mockFilterTrader2.Setup(t => t.Symbol).Returns("BTCUSDT");
        mockFilterTrader2.Setup(t => t.StartAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mockPrimaryTrader.Setup(t => t.StartAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var unit = new SymbolTradingUnit("BTCUSDT", mockPrimaryTrader.Object);
        unit.AddFilter(mockFilterTrader1.Object, new RsiSignalFilter(70m, 30m, FilterMode.Confirm));
        unit.AddFilter(mockFilterTrader2.Object, new AdxSignalFilter(20m, 30m, FilterMode.Confirm));

        // Act
        var task = unit.StartAsync(CancellationToken.None);

        // Give startup tasks time to execute
        Thread.Sleep(100);

        // Assert
        Assert.NotNull(task);
        mockPrimaryTrader.Verify(t => t.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockFilterTrader1.Verify(t => t.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockFilterTrader2.Verify(t => t.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_StopsAllTraders()
    {
        // Arrange
        var mockPrimaryTrader = new Mock<ISymbolTrader>();
        var mockFilterTrader = new Mock<ISymbolTrader>();
        mockFilterTrader.Setup(t => t.Symbol).Returns("BTCUSDT");

        mockPrimaryTrader.Setup(t => t.StopAsync()).Returns(Task.CompletedTask);
        mockFilterTrader.Setup(t => t.StopAsync()).Returns(Task.CompletedTask);

        var unit = new SymbolTradingUnit("BTCUSDT", mockPrimaryTrader.Object);
        unit.AddFilter(mockFilterTrader.Object, new RsiSignalFilter(70m, 30m, FilterMode.Confirm));

        // Act
        await unit.StopAsync();

        // Assert
        mockPrimaryTrader.Verify(t => t.StopAsync(), Times.Once);
        mockFilterTrader.Verify(t => t.StopAsync(), Times.Once);
    }

    #endregion

    #region Event Forwarding Tests

    [Fact]
    public void Events_PrimaryTraderLogEvents_AreForwarded()
    {
        // Arrange
        var mockPrimaryTrader = new Mock<ISymbolTrader>();
        var unit = new SymbolTradingUnit("BTCUSDT", mockPrimaryTrader.Object);

        string? receivedLog = null;
        unit.OnLog += msg => receivedLog = msg;

        // Act
        mockPrimaryTrader.Raise(t => t.OnLog += null, "Test log message");

        // Assert
        Assert.NotNull(receivedLog);
        Assert.Contains("Test log message", receivedLog);
    }

    [Fact]
    public void Events_PrimaryTraderSignalEvents_AreForwarded()
    {
        // Arrange
        var mockPrimaryTrader = new Mock<ISymbolTrader>();
        var unit = new SymbolTradingUnit("BTCUSDT", mockPrimaryTrader.Object);

        TradeSignal? receivedSignal = null;
        unit.OnSignal += signal => receivedSignal = signal;

        var testSignal = new TradeSignal("BTCUSDT", SignalType.Buy, 45000m, 44000m, 46000m, "Test");

        // Act
        mockPrimaryTrader.Raise(t => t.OnSignal += null, testSignal);

        // Assert
        Assert.NotNull(receivedSignal);
        Assert.Equal(testSignal, receivedSignal);
    }

    [Fact]
    public void Events_PrimaryTraderTradeEvents_AreForwarded()
    {
        // Arrange
        var mockPrimaryTrader = new Mock<ISymbolTrader>();
        var unit = new SymbolTradingUnit("BTCUSDT", mockPrimaryTrader.Object);

        Trade? receivedTrade = null;
        unit.OnTrade += trade => receivedTrade = trade;

        var testTrade = new Trade(
            Symbol: "BTCUSDT",
            EntryTime: DateTime.UtcNow,
            ExitTime: null,
            EntryPrice: 45000m,
            ExitPrice: null,
            Quantity: 0.1m,
            Direction: TradeDirection.Long,
            StopLoss: 44000m,
            TakeProfit: 46000m,
            ExitReason: null
        );

        // Act
        mockPrimaryTrader.Raise(t => t.OnTrade += null, testTrade);

        // Assert
        Assert.NotNull(receivedTrade);
        Assert.Equal(testTrade, receivedTrade);
    }

    [Fact]
    public void Events_PrimaryTraderEquityEvents_AreForwarded()
    {
        // Arrange
        var mockPrimaryTrader = new Mock<ISymbolTrader>();
        var unit = new SymbolTradingUnit("BTCUSDT", mockPrimaryTrader.Object);

        decimal? receivedEquity = null;
        unit.OnEquityUpdate += equity => receivedEquity = equity;

        // Act
        mockPrimaryTrader.Raise(t => t.OnEquityUpdate += null, 10500m);

        // Assert
        Assert.NotNull(receivedEquity);
        Assert.Equal(10500m, receivedEquity);
    }

    [Fact]
    public void Events_FilterTraderLogEvents_AreForwardedWithPrefix()
    {
        // Arrange
        var mockPrimaryTrader = new Mock<ISymbolTrader>();
        var mockFilterTrader = new Mock<ISymbolTrader>();
        mockFilterTrader.Setup(t => t.Symbol).Returns("BTCUSDT");

        var filter = new RsiSignalFilter(70m, 30m, FilterMode.Confirm);

        var unit = new SymbolTradingUnit("BTCUSDT", mockPrimaryTrader.Object);
        unit.AddFilter(mockFilterTrader.Object, filter);

        string? receivedLog = null;
        unit.OnLog += msg => receivedLog = msg;

        // Act
        mockFilterTrader.Raise(t => t.OnLog += null, "Filter log message");

        // Assert
        Assert.NotNull(receivedLog);
        Assert.Contains("[Filter:", receivedLog);
        Assert.Contains("Filter log message", receivedLog);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void SymbolTradingUnit_SupportsMultipleFilterModes()
    {
        // Arrange
        var mockPrimaryTrader = new Mock<ISymbolTrader>();
        var mockConfirmFilter = new Mock<ISymbolTrader>();
        mockConfirmFilter.Setup(t => t.Symbol).Returns("BTCUSDT");

        var mockVetoFilter = new Mock<ISymbolTrader>();
        mockVetoFilter.Setup(t => t.Symbol).Returns("BTCUSDT");

        var mockScoreFilter = new Mock<ISymbolTrader>();
        mockScoreFilter.Setup(t => t.Symbol).Returns("BTCUSDT");

        var confirmFilter = new RsiSignalFilter(70m, 30m, FilterMode.Confirm);
        var vetoFilter = new AdxSignalFilter(20m, 30m, FilterMode.Veto);
        var scoreFilter = new TrendAlignmentFilter(FilterMode.Score, false);

        var unit = new SymbolTradingUnit("BTCUSDT", mockPrimaryTrader.Object);

        // Act
        unit.AddFilter(mockConfirmFilter.Object, confirmFilter);
        unit.AddFilter(mockVetoFilter.Object, vetoFilter);
        unit.AddFilter(mockScoreFilter.Object, scoreFilter);

        // Assert
        Assert.Equal(3, unit.Filters.Count);
        Assert.Equal(FilterMode.Confirm, unit.Filters[0].Filter.Mode);
        Assert.Equal(FilterMode.Veto, unit.Filters[1].Filter.Mode);
        Assert.Equal(FilterMode.Score, unit.Filters[2].Filter.Mode);
    }

    [Fact]
    public void SymbolTradingUnit_SupportsNoFilters()
    {
        // Arrange & Act
        var mockPrimaryTrader = new Mock<ISymbolTrader>();
        var unit = new SymbolTradingUnit("BTCUSDT", mockPrimaryTrader.Object);

        // Assert
        Assert.Empty(unit.Filters);
        Assert.NotNull(unit.PrimaryTrader);
    }

    [Fact]
    public void SymbolTradingUnit_Dispose_DoesNotThrow()
    {
        // Arrange
        var mockPrimaryTrader = new Mock<ISymbolTrader>();
        var unit = new SymbolTradingUnit("BTCUSDT", mockPrimaryTrader.Object);

        // Act & Assert
        var exception = Record.Exception(() => unit.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void SymbolTradingUnit_MultipleDispose_DoesNotThrow()
    {
        // Arrange
        var mockPrimaryTrader = new Mock<ISymbolTrader>();
        var unit = new SymbolTradingUnit("BTCUSDT", mockPrimaryTrader.Object);

        // Act & Assert
        unit.Dispose();
        var exception = Record.Exception(() => unit.Dispose());
        Assert.Null(exception);
    }

    #endregion
}
