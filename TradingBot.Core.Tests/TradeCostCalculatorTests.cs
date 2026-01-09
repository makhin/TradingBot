using TradingBot.Core.Analytics;
using TradingBot.Core.Models;

namespace TradingBot.Core.Tests;

public class TradeCostCalculatorTests
{
    [Fact]
    public void CalculateFeesFromPercent_WithValidInputs_ReturnsCorrectFee()
    {
        // Arrange
        decimal price = 45000m;
        decimal quantity = 1m;
        decimal feePercent = 0.1m; // 0.1%

        // Act
        decimal fee = TradeCostCalculator.CalculateFeesFromPercent(price, quantity, feePercent);

        // Assert
        Assert.Equal(45m, fee); // 45000 * 1 * 0.1 / 100
    }

    [Fact]
    public void CalculateFeesFromPercent_WithZeroFeePercent_ReturnsZero()
    {
        // Arrange
        decimal price = 1000m;
        decimal quantity = 1m;
        decimal feePercent = 0m;

        // Act
        decimal fee = TradeCostCalculator.CalculateFeesFromPercent(price, quantity, feePercent);

        // Assert
        Assert.Equal(0m, fee);
    }

    [Fact]
    public void CalculateFeesFromPercent_WithHighFeePercent_ReturnsCorrectFee()
    {
        // Arrange
        decimal price = 1000m;
        decimal quantity = 1m;
        decimal feePercent = 1m; // 1%

        // Act
        decimal fee = TradeCostCalculator.CalculateFeesFromPercent(price, quantity, feePercent);

        // Assert
        Assert.Equal(10m, fee); // 1000 * 1 * 1 / 100
    }

    [Fact]
    public void ApplySlippage_BuyOrder_AddSlippageToPrice()
    {
        // Arrange
        decimal price = 45000m;
        decimal slippagePercent = 0.1m; // 0.1%
        var direction = TradeDirection.Long;

        // Act
        decimal resultPrice = TradeCostCalculator.ApplySlippage(price, slippagePercent, direction, isEntry: true);

        // Assert
        Assert.True(resultPrice > price); // Price should increase for buy
        Assert.Equal(45045m, resultPrice); // 45000 + (45000 * 0.1 / 100)
    }

    [Fact]
    public void ApplySlippage_SellOrder_SubtractSlippageFromPrice()
    {
        // Arrange
        decimal price = 45000m;
        decimal slippagePercent = 0.1m; // 0.1%
        var direction = TradeDirection.Short;

        // Act
        decimal resultPrice = TradeCostCalculator.ApplySlippage(price, slippagePercent, direction, isEntry: true);

        // Assert
        Assert.True(resultPrice < price); // Price should decrease for sell
        Assert.Equal(44955m, resultPrice); // 45000 - (45000 * 0.1 / 100)
    }

    [Theory]
    [InlineData(1000, 1, 0.1)]
    [InlineData(45000, 1, 0.1)]
    [InlineData(10000, 2, 0.1)]
    public void CalculateFeesFromPercent_WithVariousInputs_CalculatesCorrectly(
        decimal price,
        decimal quantity,
        decimal feePercent)
    {
        // Act
        decimal fee = TradeCostCalculator.CalculateFeesFromPercent(price, quantity, feePercent);
        decimal expectedFee = price * quantity * feePercent / 100;

        // Assert
        Assert.Equal(expectedFee, fee, precision: 2);
    }

    [Fact]
    public void CalculateTotalCosts_WithBothFeesAndSlippage_CalculatesCorrectly()
    {
        // Arrange
        decimal entryPrice = 45000m;
        decimal exitPrice = 46000m;
        decimal quantity = 1m;
        decimal feeRate = 0.1m; // 0.1%
        decimal slippageRate = 0.1m; // 0.1%

        // Act
        decimal totalCosts = TradeCostCalculator.CalculateTotalCosts(entryPrice, exitPrice, quantity, feeRate, slippageRate);

        // Assert
        Assert.True(totalCosts > 0);
        // notional = (45000 + 46000) * 1 = 91000
        // totalCosts = 91000 * (0.1 + 0.1) = 91000 * 0.2 = 18200
        Assert.Equal(18200m, totalCosts);
    }
}
