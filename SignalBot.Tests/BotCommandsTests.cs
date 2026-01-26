using SignalBot.Configuration;
using SignalBot.Models;
using SignalBot.Services;
using SignalBot.Services.Commands;
using SignalBot.Services.Statistics;
using SignalBot.Services.Trading;
using SignalBot.State;
using TradingBot.Binance.Futures.Interfaces;
using TradingBot.Core.Models;
using Moq;
using Xunit;
using Serilog;
using Microsoft.Extensions.Options;

namespace SignalBot.Tests;

public class BotCommandsTests
{
    private readonly BotController _controller;
    private readonly CooldownManager _cooldownManager;
    private readonly Mock<IPositionManager> _mockPositionManager;
    private readonly Mock<IPositionStore<SignalPosition>> _mockStore;
    private readonly Mock<IFuturesOrderExecutor> _mockOrderExecutor;
    private readonly Mock<IBinanceFuturesClient> _mockClient;
    private readonly Mock<ITradeStatisticsService> _mockTradeStatistics;
    private readonly TelegramBotCommands _commands;
    private readonly ILogger _logger;

    public BotCommandsTests()
    {
        _logger = new LoggerConfiguration().CreateLogger();
        _controller = new BotController(_logger);
        _cooldownManager = new CooldownManager(new CooldownSettings(), _logger);
        _mockPositionManager = new Mock<IPositionManager>();
        _mockStore = new Mock<IPositionStore<SignalPosition>>();
        _mockOrderExecutor = new Mock<IFuturesOrderExecutor>();
        _mockClient = new Mock<IBinanceFuturesClient>();
        _mockTradeStatistics = new Mock<ITradeStatisticsService>();

        var settings = Options.Create(new SignalBotSettings
        {
            Trading = new TradingSettings
            {
                DefaultSymbolSuffix = "USDT"
            }
        });

        _commands = new TelegramBotCommands(
            _controller,
            _cooldownManager,
            _mockPositionManager.Object,
            _mockStore.Object,
            _mockOrderExecutor.Object,
            _mockClient.Object,
            _mockTradeStatistics.Object,
            settings,
            _logger);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsStatus()
    {
        // Arrange
        var positions = new List<SignalPosition>
        {
            CreateTestPosition("BTCUSDT", 100m, 50m)
        };

        _mockStore.Setup(x => x.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions);

        _mockClient.Setup(x => x.GetBalanceAsync("USDT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(10000m);

        // Act
        var result = await _commands.GetStatusAsync();

        // Assert
        Assert.Contains("SignalBot Status", result);
        Assert.Contains("10000", result);
        Assert.Contains("Open positions: `1`", result);
    }

    [Fact]
    public async Task GetPositionsAsync_NoPositions_ReturnsEmptyMessage()
    {
        // Arrange
        _mockStore.Setup(x => x.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SignalPosition>());

        // Act
        var result = await _commands.GetPositionsAsync();

        // Assert
        Assert.Contains("No open positions", result);
    }

    [Fact]
    public async Task GetPositionsAsync_WithPositions_ReturnsPositionList()
    {
        // Arrange
        var positions = new List<SignalPosition>
        {
            CreateTestPosition("BTCUSDT", 100m, 50m),
            CreateTestPosition("ETHUSDT", 50m, 25m)
        };

        _mockStore.Setup(x => x.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions);

        // Act
        var result = await _commands.GetPositionsAsync();

        // Assert
        Assert.Contains("BTCUSDT", result);
        Assert.Contains("ETHUSDT", result);
        Assert.Contains("LONG", result);
    }

    [Fact]
    public async Task PauseAsync_SetsModeToPaused()
    {
        // Act
        var result = await _commands.PauseAsync();

        // Assert
        Assert.Contains("Paused", result);
        Assert.Equal(BotOperatingMode.Paused, _controller.CurrentMode);
    }

    [Fact]
    public async Task ResumeAsync_SetsModeToAutomatic()
    {
        // Act
        var result = await _commands.ResumeAsync();

        // Assert
        Assert.Contains("Resumed", result);
        Assert.Equal(BotOperatingMode.Automatic, _controller.CurrentMode);
    }

    [Fact]
    public async Task EmergencyStopAsync_SetsModeToEmergencyStop()
    {
        // Arrange
        _mockStore.Setup(x => x.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SignalPosition>());

        // Act
        var result = await _commands.EmergencyStopAsync();

        // Assert
        Assert.Contains("EMERGENCY STOP", result);
        Assert.Equal(BotOperatingMode.EmergencyStop, _controller.CurrentMode);
    }

    [Fact]
    public void GetHelp_ReturnsHelpText()
    {
        // Act
        var result = _commands.GetHelp();

        // Assert
        Assert.Contains("/status", result);
        Assert.Contains("/pause", result);
        Assert.Contains("/resume", result);
        Assert.Contains("/closeall", result);
        Assert.Contains("/help", result);
    }

    [Fact]
    public async Task ClosePositionAsync_PositionNotFound_ReturnsError()
    {
        // Arrange
        _mockStore.Setup(x => x.GetPositionBySymbolAsync("BTCUSDT", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SignalPosition?)null);

        // Act
        var result = await _commands.ClosePositionAsync("BTCUSDT");

        // Assert
        Assert.Contains("not found", result);
    }

    [Fact]
    public void BotController_CanAcceptNewSignals_InAutomaticMode()
    {
        // Arrange
        var controller = new BotController(_logger);

        // Act
        var canAccept = controller.CanAcceptNewSignals();

        // Assert
        Assert.True(canAccept);
        Assert.Equal(BotOperatingMode.Automatic, controller.CurrentMode);
    }

    [Fact]
    public void BotController_CannotAcceptNewSignals_InPausedMode()
    {
        // Arrange
        var controller = new BotController(_logger);
        controller.SetMode(BotOperatingMode.Paused);

        // Act
        var canAccept = controller.CanAcceptNewSignals();

        // Assert
        Assert.False(canAccept);
    }

    [Fact]
    public void BotController_CanManagePositions_InMonitorOnlyMode()
    {
        // Arrange
        var controller = new BotController(_logger);
        controller.SetMode(BotOperatingMode.MonitorOnly);

        // Act
        var canManage = controller.CanManagePositions();
        var canAccept = controller.CanAcceptNewSignals();

        // Assert
        Assert.True(canManage);
        Assert.False(canAccept);
    }

    [Fact]
    public void BotController_ModeChanged_TriggersEvent()
    {
        // Arrange
        var controller = new BotController(_logger);
        BotOperatingMode? newMode = null;
        controller.OnModeChanged += (sender, mode) => newMode = mode;

        // Act
        controller.SetMode(BotOperatingMode.Paused);

        // Assert
        Assert.Equal(BotOperatingMode.Paused, newMode);
    }

    private SignalPosition CreateTestPosition(string symbol, decimal entryPrice, decimal quantity)
    {
        return new SignalPosition
        {
            SignalId = Guid.NewGuid(),
            Symbol = symbol,
            Direction = SignalDirection.Long,
            Status = PositionStatus.Open,
            PlannedEntryPrice = entryPrice,
            ActualEntryPrice = entryPrice,
            CurrentStopLoss = entryPrice * 0.95m,
            Leverage = 10,
            InitialQuantity = quantity,
            RemainingQuantity = quantity,
            Targets = new List<TargetLevel>
            {
                new TargetLevel
                {
                    Index = 0,
                    Price = entryPrice * 1.05m,
                    PercentToClose = 50m,
                    QuantityToClose = quantity * 0.5m
                }
            },
            RealizedPnl = 100m,
            UnrealizedPnl = 50m
        };
    }
}
