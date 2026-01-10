using SignalBot.Configuration;
using SignalBot.Models;
using SignalBot.Services;
using Serilog;
using Xunit;

namespace SignalBot.Tests;

public class CooldownManagerTests
{
    private readonly ILogger _logger;

    public CooldownManagerTests()
    {
        _logger = new LoggerConfiguration().CreateLogger();
    }

    [Fact]
    public void Constructor_InitializesWithDefaultState()
    {
        // Arrange
        var settings = new CooldownSettings();

        // Act
        var manager = new CooldownManager(settings, _logger);
        var status = manager.GetStatus();

        // Assert
        Assert.False(status.IsInCooldown);
        Assert.Null(status.CooldownUntil);
        Assert.Null(status.RemainingTime);
        Assert.Equal(0, status.ConsecutiveLosses);
        Assert.Equal(1.0m, status.CurrentSizeMultiplier);
    }

    [Fact]
    public void OnPositionClosed_StopLoss_ActivatesCooldown()
    {
        // Arrange
        var settings = new CooldownSettings
        {
            Enabled = true,
            CooldownAfterStopLoss = TimeSpan.FromMinutes(15)
        };
        var manager = new CooldownManager(settings, _logger);

        var position = CreateTestPosition(PositionCloseReason.StopLossHit);

        // Act
        manager.OnPositionClosed(position);
        var status = manager.GetStatus();

        // Assert
        Assert.True(status.IsInCooldown);
        Assert.NotNull(status.CooldownUntil);
        Assert.Equal(1, status.ConsecutiveLosses);
        Assert.Equal(0.75m, status.CurrentSizeMultiplier); // After 1 loss
    }

    [Fact]
    public void OnPositionClosed_MultipleStopLosses_IncreasesConsecutiveLosses()
    {
        // Arrange
        var settings = new CooldownSettings
        {
            Enabled = true,
            ConsecutiveLossesForLongCooldown = 3,
            CooldownAfterStopLoss = TimeSpan.FromMinutes(15),
            LongCooldownDuration = TimeSpan.FromHours(2)
        };
        var manager = new CooldownManager(settings, _logger);

        // Act
        manager.OnPositionClosed(CreateTestPosition(PositionCloseReason.StopLossHit));
        manager.OnPositionClosed(CreateTestPosition(PositionCloseReason.StopLossHit));
        var status = manager.GetStatus();

        // Assert
        Assert.Equal(2, status.ConsecutiveLosses);
        Assert.Equal(0.5m, status.CurrentSizeMultiplier); // After 2 losses
    }

    [Fact]
    public void OnPositionClosed_ThreeStopLosses_ActivatesLongCooldown()
    {
        // Arrange
        var settings = new CooldownSettings
        {
            Enabled = true,
            ConsecutiveLossesForLongCooldown = 3,
            CooldownAfterStopLoss = TimeSpan.FromMinutes(15),
            LongCooldownDuration = TimeSpan.FromHours(2)
        };
        var manager = new CooldownManager(settings, _logger);

        // Act
        manager.OnPositionClosed(CreateTestPosition(PositionCloseReason.StopLossHit));
        manager.OnPositionClosed(CreateTestPosition(PositionCloseReason.StopLossHit));
        manager.OnPositionClosed(CreateTestPosition(PositionCloseReason.StopLossHit));
        var status = manager.GetStatus();

        // Assert
        Assert.Equal(3, status.ConsecutiveLosses);
        Assert.Equal(0.25m, status.CurrentSizeMultiplier); // After 3+ losses
        Assert.True(status.IsInCooldown);
        // Cooldown should be close to 2 hours
        Assert.True(status.RemainingTime >= TimeSpan.FromMinutes(119));
    }

    [Fact]
    public void OnPositionClosed_Liquidation_ActivatesLongCooldown()
    {
        // Arrange
        var settings = new CooldownSettings
        {
            Enabled = true,
            CooldownAfterLiquidation = TimeSpan.FromHours(1)
        };
        var manager = new CooldownManager(settings, _logger);

        var position = CreateTestPosition(PositionCloseReason.Liquidation);

        // Act
        manager.OnPositionClosed(position);
        var status = manager.GetStatus();

        // Assert
        Assert.True(status.IsInCooldown);
        Assert.Equal(1, status.ConsecutiveLosses);
        // Cooldown should be close to 1 hour
        Assert.True(status.RemainingTime >= TimeSpan.FromMinutes(59));
    }

    [Fact]
    public void OnPositionClosed_AllTargetsHit_ResetsLossCounter()
    {
        // Arrange
        var settings = new CooldownSettings
        {
            Enabled = true,
            WinsToResetLossCounter = 2
        };
        var manager = new CooldownManager(settings, _logger);

        // Add 2 losses first
        manager.OnPositionClosed(CreateTestPosition(PositionCloseReason.StopLossHit));
        manager.OnPositionClosed(CreateTestPosition(PositionCloseReason.StopLossHit));
        Assert.Equal(2, manager.GetStatus().ConsecutiveLosses);

        // Act - Add 2 wins
        manager.OnPositionClosed(CreateTestPosition(PositionCloseReason.AllTargetsHit));
        var statusAfterFirstWin = manager.GetStatus();
        manager.OnPositionClosed(CreateTestPosition(PositionCloseReason.AllTargetsHit));
        var statusAfterSecondWin = manager.GetStatus();

        // Assert
        Assert.Equal(2, statusAfterFirstWin.ConsecutiveLosses); // Still 2 after first win
        Assert.Equal(0, statusAfterSecondWin.ConsecutiveLosses); // Reset after 2 wins
        Assert.Equal(1.0m, statusAfterSecondWin.CurrentSizeMultiplier); // Back to normal size
    }

    [Fact]
    public void OnPositionClosed_ManualClose_DoesNotAffectCooldown()
    {
        // Arrange
        var settings = new CooldownSettings { Enabled = true };
        var manager = new CooldownManager(settings, _logger);

        var position = CreateTestPosition(PositionCloseReason.ManualClose);

        // Act
        manager.OnPositionClosed(position);
        var status = manager.GetStatus();

        // Assert
        Assert.False(status.IsInCooldown);
        Assert.Equal(0, status.ConsecutiveLosses);
        Assert.Equal(1.0m, status.CurrentSizeMultiplier);
    }

    [Fact]
    public void OnPositionClosed_DisabledCooldown_DoesNothing()
    {
        // Arrange
        var settings = new CooldownSettings { Enabled = false };
        var manager = new CooldownManager(settings, _logger);

        var position = CreateTestPosition(PositionCloseReason.StopLossHit);

        // Act
        manager.OnPositionClosed(position);
        var status = manager.GetStatus();

        // Assert
        Assert.False(status.IsInCooldown);
        Assert.Equal(0, status.ConsecutiveLosses); // Counter still increments even when disabled
    }

    [Fact]
    public void ForceResetCooldown_ClearsCooldownPeriod()
    {
        // Arrange
        var settings = new CooldownSettings
        {
            Enabled = true,
            CooldownAfterStopLoss = TimeSpan.FromMinutes(15)
        };
        var manager = new CooldownManager(settings, _logger);

        manager.OnPositionClosed(CreateTestPosition(PositionCloseReason.StopLossHit));
        Assert.True(manager.GetStatus().IsInCooldown);

        // Act
        manager.ForceResetCooldown();
        var status = manager.GetStatus();

        // Assert
        Assert.False(status.IsInCooldown);
        Assert.Null(status.CooldownUntil);
        Assert.Null(status.RemainingTime);
        // Note: Consecutive losses counter is NOT reset
        Assert.Equal(1, status.ConsecutiveLosses);
    }

    [Fact]
    public void ForceResetLossCounter_ResetsConsecutiveLosses()
    {
        // Arrange
        var settings = new CooldownSettings { Enabled = true };
        var manager = new CooldownManager(settings, _logger);

        manager.OnPositionClosed(CreateTestPosition(PositionCloseReason.StopLossHit));
        manager.OnPositionClosed(CreateTestPosition(PositionCloseReason.StopLossHit));
        Assert.Equal(2, manager.GetStatus().ConsecutiveLosses);

        // Act
        manager.ForceResetLossCounter();
        var status = manager.GetStatus();

        // Assert
        Assert.Equal(0, status.ConsecutiveLosses);
        Assert.Equal(1.0m, status.CurrentSizeMultiplier);
    }

    [Fact]
    public void GetCurrentSizeMultiplier_ReturnsCorrectMultiplier()
    {
        // Arrange
        var settings = new CooldownSettings
        {
            Enabled = true,
            ReduceSizeAfterLosses = true,
            SizeMultiplierAfter1Loss = 0.75m,
            SizeMultiplierAfter2Losses = 0.5m,
            SizeMultiplierAfter3PlusLosses = 0.25m
        };
        var manager = new CooldownManager(settings, _logger);

        // Act & Assert
        Assert.Equal(1.0m, manager.GetCurrentSizeMultiplier()); // 0 losses

        manager.OnPositionClosed(CreateTestPosition(PositionCloseReason.StopLossHit));
        Assert.Equal(0.75m, manager.GetCurrentSizeMultiplier()); // 1 loss

        manager.OnPositionClosed(CreateTestPosition(PositionCloseReason.StopLossHit));
        Assert.Equal(0.5m, manager.GetCurrentSizeMultiplier()); // 2 losses

        manager.OnPositionClosed(CreateTestPosition(PositionCloseReason.StopLossHit));
        Assert.Equal(0.25m, manager.GetCurrentSizeMultiplier()); // 3 losses

        manager.OnPositionClosed(CreateTestPosition(PositionCloseReason.StopLossHit));
        Assert.Equal(0.25m, manager.GetCurrentSizeMultiplier()); // 4+ losses (still 0.25)
    }

    [Fact]
    public void GetCurrentSizeMultiplier_DisabledReduction_ReturnsOne()
    {
        // Arrange
        var settings = new CooldownSettings
        {
            Enabled = true,
            ReduceSizeAfterLosses = false
        };
        var manager = new CooldownManager(settings, _logger);

        // Act
        manager.OnPositionClosed(CreateTestPosition(PositionCloseReason.StopLossHit));
        manager.OnPositionClosed(CreateTestPosition(PositionCloseReason.StopLossHit));

        // Assert
        Assert.Equal(1.0m, manager.GetCurrentSizeMultiplier()); // Always 1.0 when disabled
    }

    [Fact]
    public void IsInCooldown_ReturnsFalseAfterCooldownExpires()
    {
        // Arrange
        var settings = new CooldownSettings
        {
            Enabled = true,
            CooldownAfterStopLoss = TimeSpan.FromMilliseconds(100) // Very short cooldown
        };
        var manager = new CooldownManager(settings, _logger);

        manager.OnPositionClosed(CreateTestPosition(PositionCloseReason.StopLossHit));
        Assert.True(manager.IsInCooldown);

        // Act - Wait for cooldown to expire
        Thread.Sleep(150);

        // Assert
        Assert.False(manager.IsInCooldown);
        var status = manager.GetStatus();
        Assert.False(status.IsInCooldown);
        Assert.Null(status.RemainingTime);
    }

    private SignalPosition CreateTestPosition(PositionCloseReason closeReason)
    {
        return new SignalPosition
        {
            SignalId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Direction = SignalDirection.Long,
            Status = PositionStatus.Closed,
            PlannedEntryPrice = 50000m,
            ActualEntryPrice = 50000m,
            CurrentStopLoss = 49000m,
            Leverage = 10,
            InitialQuantity = 0.1m,
            RemainingQuantity = 0m,
            Targets = new List<TargetLevel>(),
            RealizedPnl = closeReason == PositionCloseReason.AllTargetsHit ? 100m : -50m,
            UnrealizedPnl = 0m,
            CloseReason = closeReason,
            ClosedAt = DateTime.UtcNow
        };
    }
}
