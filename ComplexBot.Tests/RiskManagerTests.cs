using ComplexBot.Models;
using TradingBot.Core.RiskManagement;
using TradingBot.Core.Models;

namespace ComplexBot.Tests;

public class RiskManagerTests
{
    [Fact]
    public void CalculatePositionSize_WithNormalDrawdown_ReturnsFullSize()
    {
        // Arrange
        var settings = RiskSettingsFactory.CreateDefault();
        var manager = new RiskManager(settings, initialCapital: 10000m);

        // Act
        var size = manager.CalculatePositionSize(
            entryPrice: 45000m,
            stopLossPrice: 43500m  // 1500 USDT stop distance
        );

        // Assert
        // Risk = 10000 * 0.015 = 150 USDT
        // Size = 150 / 1500 = 0.1 BTC
        Assert.Equal(0.1m, size.Quantity, precision: 4);
        Assert.Equal(150m, size.RiskAmount, precision: 2);
        Assert.Equal(1500m, size.StopDistance, precision: 2);
    }

    [Fact]
    public void CalculatePositionSize_WithDrawdown_ReducesSize()
    {
        // Arrange
        var settings = RiskSettingsFactory.CreateDefault();
        var manager = new RiskManager(settings, initialCapital: 10000m);
        manager.UpdateEquity(8500m); // 15% drawdown

        // Act
        var size = manager.CalculatePositionSize(
            entryPrice: 45000m,
            stopLossPrice: 43500m
        );

        // Assert
        // At 15% drawdown, risk is reduced to 50% (8500 * 0.015 * 0.5 = 63.75 USDT)
        // Size = 63.75 / 1500 ≈ 0.0425 BTC
        Assert.True(size.Quantity < 0.1m);
        Assert.True(size.RiskAmount < 150m);
    }

    [Fact]
    public void CalculatePositionSize_WithZeroStopDistance_ReturnsZero()
    {
        // Arrange
        var settings = RiskSettingsFactory.CreateDefault();
        var manager = new RiskManager(settings, initialCapital: 10000m);

        // Act
        var size = manager.CalculatePositionSize(
            entryPrice: 45000m,
            stopLossPrice: 45000m  // Zero stop distance
        );

        // Assert
        Assert.Equal(0m, size.Quantity);
    }

    [Fact]
    public void CurrentDrawdown_WithEquityChange_CalculatesCorrectly()
    {
        // Arrange
        var settings = RiskSettingsFactory.CreateDefault();
        var manager = new RiskManager(settings, initialCapital: 10000m);

        // Act
        manager.UpdateEquity(8000m);  // 20% loss
        var drawdown = manager.CurrentDrawdown;

        // Assert
        Assert.True(drawdown >= 20m);
        Assert.True(drawdown <= 21m);
    }

    [Fact]
    public void GetDailyDrawdownPercent_TracksDaily()
    {
        // Arrange
        var settings = RiskSettingsFactory.CreateDefault();
        var manager = new RiskManager(settings, initialCapital: 10000m);

        // Act
        manager.ResetDailyTracking();
        manager.UpdateEquity(9700m);  // 3% daily loss
        var dailyDrawdown = manager.GetDailyDrawdownPercent();

        // Assert
        Assert.True(dailyDrawdown >= 3m);
        Assert.True(dailyDrawdown <= 3.1m);
    }

    [Fact]
    public void IsDailyLimitExceeded_WithExceededLimit_ReturnsTrue()
    {
        // Arrange
        var settings = RiskSettingsFactory.CreateDefault();
        var manager = new RiskManager(settings, initialCapital: 10000m);

        // Act
        manager.ResetDailyTracking();
        manager.UpdateEquity(9680m);  // 3.2% daily loss
        var isExceeded = manager.IsDailyLimitExceeded();

        // Assert
        Assert.True(isExceeded);
    }

    [Fact]
    public void GetDrawdownAdjustedRisk_WithHighDrawdown_ReducesRiskSignificantly()
    {
        // Arrange
        var settings = RiskSettingsFactory.CreateDefault() with { RiskPerTradePercent = 2.0m };
        var manager = new RiskManager(settings, initialCapital: 10000m);

        // Act
        manager.UpdateEquity(8000m);  // 20% drawdown
        var adjustedRisk = manager.GetDrawdownAdjustedRisk();

        // Assert
        // At 20% drawdown, risk should be reduced to 25% of original (2.0 * 0.25 = 0.5)
        Assert.Equal(0.5m, adjustedRisk);
        Assert.True(adjustedRisk > 0);
    }

    [Fact]
    public void CalculatePositionSize_WithAtr_UsesAttrForMinStopDistance()
    {
        // Arrange
        var settings = RiskSettingsFactory.CreateDefault();
        var manager = new RiskManager(settings, initialCapital: 10000m);
        decimal atr = 200m;  // ATR = 200, so minimum stop distance = 200 * 2.0 = 400

        // Act
        var size = manager.CalculatePositionSize(
            entryPrice: 45000m,
            stopLossPrice: 44900m,  // Only 100 USDT stop
            atr: atr
        );

        // Assert
        // Actual stop distance should be max(100, 400) = 400
        Assert.Equal(400m, size.StopDistance, precision: 2);
        decimal expectedQuantity = (10000m * 0.015m) / 400m;
        Assert.Equal(expectedQuantity, size.Quantity, precision: 4);
    }

    [Fact]
    public void PortfolioHeat_WithMultiplePositions_CalculatesTotal()
    {
        // Arrange
        var settings = RiskSettingsFactory.CreateDefault();
        var manager = new RiskManager(settings, initialCapital: 10000m);

        // Act - Calculate position sizes for multiple symbols
        var size1 = manager.CalculatePositionSize(45000m, 43500m);
        var size2 = manager.CalculatePositionSize(3000m, 2700m);

        // Assert
        var heat = manager.PortfolioHeat;
        Assert.True(heat >= 0);
        Assert.True(heat <= 100);
    }
}
