using SignalBot.Configuration;
using SignalBot.Models;
using SignalBot.Services.Trading;
using SignalBot.Services.Validation;
using SignalBot.State;
using TradingBot.Core.Exchanges;
using TradingBot.Core.Models;
using TradingBot.Core.RiskManagement;
using Moq;
using Xunit;
using Serilog;
using Polly;

namespace SignalBot.Tests;

public class PriceDeviationTests
{
    private readonly Mock<IFuturesExchangeClient> _mockClient;
    private readonly Mock<IFuturesOrderExecutor> _mockOrderExecutor;
    private readonly Mock<IPositionManager> _mockPositionManager;
    private readonly Mock<IRiskManager> _mockRiskManager;
    private readonly IAsyncPolicy<ExecutionResult> _retryPolicy;
    private readonly ILogger _logger;

    public PriceDeviationTests()
    {
        _mockClient = new Mock<IFuturesExchangeClient>();
        _mockOrderExecutor = new Mock<IFuturesOrderExecutor>();
        _mockPositionManager = new Mock<IPositionManager>();
        _mockRiskManager = new Mock<IRiskManager>();
        _retryPolicy = Policy.NoOpAsync<ExecutionResult>();
        _logger = new LoggerConfiguration().CreateLogger();
    }

    [Fact]
    public async Task ExecuteSignal_PriceWithinDeviation_ShouldExecute()
    {
        // Arrange
        var entrySettings = new EntrySettings
        {
            MaxPriceDeviationPercent = 0.5m,
            DeviationAction = PriceDeviationAction.Skip
        };

        var tradingSettings = new TradingSettings
        {
            TargetClosePercents = new List<decimal> { 25, 25, 25, 25 },
            MoveStopToBreakeven = true
        };

        var signal = CreateTestSignal(entry: 100m, targets: new[] { 101m, 102m, 103m, 104m });

        // Mock current price within deviation (0.3%)
        _mockClient.Setup(x => x.GetMarkPriceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(100.3m);

        _mockClient.Setup(x => x.SetLeverageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockClient.Setup(x => x.SetMarginTypeAsync(It.IsAny<string>(), It.IsAny<MarginType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockRiskManager.Setup(x => x.CalculatePositionSize(
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal?>()))
            .Returns(new PositionSizeResult(Quantity: 1.0m, RiskAmount: 100m, StopDistance: 0.05m));

        _mockOrderExecutor.Setup(x => x.PlaceMarketOrderAsync(
                It.IsAny<string>(),
                It.IsAny<TradingBot.Core.Models.TradeDirection>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                Success = true,
                OrderId = 12345,
                AveragePrice = 100.3m
            });

        _mockOrderExecutor.Setup(x => x.PlaceStopLossAsync(
                It.IsAny<string>(),
                It.IsAny<TradingBot.Core.Models.TradeDirection>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                Success = true,
                OrderId = 12346
            });

        _mockOrderExecutor.Setup(x => x.PlaceTakeProfitAsync(
                It.IsAny<string>(),
                It.IsAny<TradingBot.Core.Models.TradeDirection>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                Success = true,
                OrderId = 12347
            });

        var trader = new SignalTrader(
            _mockClient.Object,
            _mockOrderExecutor.Object,
            _mockPositionManager.Object,
            _mockRiskManager.Object,
            tradingSettings,
            entrySettings,
            _retryPolicy,
            _logger);

        // Act
        var position = await trader.ExecuteSignalAsync(signal, 10000m);

        // Assert
        Assert.Equal(PositionStatus.Open, position.Status);
        Assert.Equal(100.3m, position.ActualEntryPrice);
    }

    [Fact]
    public async Task ExecuteSignal_MoveStopToBreakeven_ShouldSetMoveStopLossTargets()
    {
        // Arrange
        var entrySettings = new EntrySettings
        {
            MaxPriceDeviationPercent = 1.0m,
            DeviationAction = PriceDeviationAction.Skip
        };

        var tradingSettings = new TradingSettings
        {
            TargetClosePercents = new List<decimal> { 50, 30, 20 },
            MoveStopToBreakeven = true
        };

        var signal = CreateTestSignal(entry: 100m, targets: new[] { 110m, 120m, 130m });

        _mockClient.Setup(x => x.GetMarkPriceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(100m);

        _mockClient.Setup(x => x.SetLeverageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockClient.Setup(x => x.SetMarginTypeAsync(It.IsAny<string>(), It.IsAny<MarginType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockRiskManager.Setup(x => x.CalculatePositionSize(
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal?>()))
            .Returns(new PositionSizeResult(Quantity: 1.0m, RiskAmount: 100m, StopDistance: 0.05m));

        _mockOrderExecutor.Setup(x => x.PlaceMarketOrderAsync(
                It.IsAny<string>(),
                It.IsAny<TradingBot.Core.Models.TradeDirection>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                Success = true,
                OrderId = 12345,
                AveragePrice = 100m
            });

        _mockOrderExecutor.Setup(x => x.PlaceStopLossAsync(
                It.IsAny<string>(),
                It.IsAny<TradingBot.Core.Models.TradeDirection>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                Success = true,
                OrderId = 12346
            });

        _mockOrderExecutor.Setup(x => x.PlaceTakeProfitAsync(
                It.IsAny<string>(),
                It.IsAny<TradingBot.Core.Models.TradeDirection>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                Success = true,
                OrderId = 12347
            });

        var trader = new SignalTrader(
            _mockClient.Object,
            _mockOrderExecutor.Object,
            _mockPositionManager.Object,
            _mockRiskManager.Object,
            tradingSettings,
            entrySettings,
            _retryPolicy,
            _logger);

        // Act
        var position = await trader.ExecuteSignalAsync(signal, 10000m);

        // Assert
        Assert.Equal(signal.Entry, position.Targets[0].MoveStopLossTo);
        Assert.Equal(signal.Targets[0], position.Targets[1].MoveStopLossTo);
        Assert.Equal(signal.Targets[1], position.Targets[2].MoveStopLossTo);
    }

    [Fact]
    public async Task ExecuteSignal_PriceExceedsDeviation_Skip_ShouldCancel()
    {
        // Arrange
        var entrySettings = new EntrySettings
        {
            MaxPriceDeviationPercent = 0.5m,
            DeviationAction = PriceDeviationAction.Skip
        };

        var tradingSettings = new TradingSettings
        {
            TargetClosePercents = new List<decimal> { 25, 25, 25, 25 },
            MoveStopToBreakeven = true
        };

        var signal = CreateTestSignal(entry: 100m, targets: new[] { 101m, 102m, 103m, 104m });

        // Mock current price beyond deviation (1.5%)
        _mockClient.Setup(x => x.GetMarkPriceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(101.5m);

        _mockClient.Setup(x => x.SetLeverageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockClient.Setup(x => x.SetMarginTypeAsync(It.IsAny<string>(), It.IsAny<MarginType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var trader = new SignalTrader(
            _mockClient.Object,
            _mockOrderExecutor.Object,
            _mockPositionManager.Object,
            _mockRiskManager.Object,
            tradingSettings,
            entrySettings,
            _retryPolicy,
            _logger);

        // Act
        var result = await trader.ExecuteSignalAsync(signal, 10000m);

        // Assert
        Assert.Equal(PositionStatus.Cancelled, result.Status);

        _mockPositionManager.Verify(
            x => x.SavePositionAsync(
                It.Is<SignalPosition>(p => p.Status == PositionStatus.Cancelled),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteSignal_PriceExceedsDeviation_AdjustTargets_ShouldAdjust()
    {
        // Arrange
        var entrySettings = new EntrySettings
        {
            MaxPriceDeviationPercent = 0.5m,
            DeviationAction = PriceDeviationAction.EnterAndAdjustTargets
        };

        var tradingSettings = new TradingSettings
        {
            TargetClosePercents = new List<decimal> { 25, 25, 25, 25 },
            MoveStopToBreakeven = true
        };

        var signal = CreateTestSignal(entry: 100m, targets: new[] { 101m, 102m, 103m, 104m });

        // Mock current price beyond deviation (1.0%)
        _mockClient.Setup(x => x.GetMarkPriceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(101m);

        _mockClient.Setup(x => x.SetLeverageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockClient.Setup(x => x.SetMarginTypeAsync(It.IsAny<string>(), It.IsAny<MarginType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockRiskManager.Setup(x => x.CalculatePositionSize(
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal?>()))
            .Returns(new PositionSizeResult(Quantity: 1.0m, RiskAmount: 100m, StopDistance: 0.05m));

        _mockOrderExecutor.Setup(x => x.PlaceMarketOrderAsync(
                It.IsAny<string>(),
                It.IsAny<TradingBot.Core.Models.TradeDirection>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                Success = true,
                OrderId = 12345,
                AveragePrice = 101m
            });

        _mockOrderExecutor.Setup(x => x.PlaceStopLossAsync(
                It.IsAny<string>(),
                It.IsAny<TradingBot.Core.Models.TradeDirection>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                Success = true,
                OrderId = 12346
            });

        _mockOrderExecutor.Setup(x => x.PlaceTakeProfitAsync(
                It.IsAny<string>(),
                It.IsAny<TradingBot.Core.Models.TradeDirection>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                Success = true,
                OrderId = 12347
            });

        var trader = new SignalTrader(
            _mockClient.Object,
            _mockOrderExecutor.Object,
            _mockPositionManager.Object,
            _mockRiskManager.Object,
            tradingSettings,
            entrySettings,
            _retryPolicy,
            _logger);

        // Act
        var position = await trader.ExecuteSignalAsync(signal, 10000m);

        // Assert
        Assert.Equal(PositionStatus.Open, position.Status);

        // Targets should be shifted by +1 (101 - 100)
        Assert.Equal(102m, position.Targets[0].Price); // Was 101, now 102
        Assert.Equal(103m, position.Targets[1].Price); // Was 102, now 103
        Assert.Equal(104m, position.Targets[2].Price); // Was 103, now 104
        Assert.Equal(105m, position.Targets[3].Price); // Was 104, now 105
    }

    private TradingSignal CreateTestSignal(decimal entry, decimal[] targets)
    {
        return new TradingSignal
        {
            RawText = "Test signal",
            Source = new SignalSource
            {
                ChannelName = "Test",
                ChannelId = 123,
                MessageId = 456
            },
            Symbol = "BTCUSDT",
            Direction = SignalDirection.Long,
            Entry = entry,
            OriginalStopLoss = entry * 0.95m,
            Targets = targets,
            OriginalLeverage = 10,
            AdjustedStopLoss = entry * 0.95m,
            AdjustedLeverage = 10,
            IsValid = true
        };
    }
}
