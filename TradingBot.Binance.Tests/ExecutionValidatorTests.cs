using Binance.Net.Enums;
using TradingBot.Binance.Common;
using TradingBot.Binance.Common.Models;

namespace TradingBot.Binance.Tests;

public class ExecutionValidatorTests
{
    [Fact]
    public void ValidateExecution_BuyWithNoSlippage_IsAcceptable()
    {
        // Arrange
        var validator = new ExecutionValidator(maxSlippagePercent: 1.0m);
        decimal expectedPrice = 45000m;
        decimal actualPrice = 45000m;

        // Act
        var result = validator.ValidateExecution(expectedPrice, actualPrice, OrderSide.Buy);

        // Assert
        Assert.True(result.IsAcceptable);
        Assert.Equal(0m, result.SlippagePercent);
        Assert.Null(result.RejectReason);
    }

    [Fact]
    public void ValidateExecution_BuyWithAcceptableSlippage_IsAcceptable()
    {
        // Arrange
        var validator = new ExecutionValidator(maxSlippagePercent: 1.0m);
        decimal expectedPrice = 45000m;
        decimal actualPrice = 45450m; // 1% slippage

        // Act
        var result = validator.ValidateExecution(expectedPrice, actualPrice, OrderSide.Buy);

        // Assert
        Assert.True(result.IsAcceptable);
        Assert.Equal(1m, result.SlippagePercent, precision: 1);
    }

    [Fact]
    public void ValidateExecution_BuyWithExcessiveSlippage_IsRejected()
    {
        // Arrange
        var validator = new ExecutionValidator(maxSlippagePercent: 1.0m);
        decimal expectedPrice = 45000m;
        decimal actualPrice = 45900m; // 2% slippage

        // Act
        var result = validator.ValidateExecution(expectedPrice, actualPrice, OrderSide.Buy);

        // Assert
        Assert.False(result.IsAcceptable);
        Assert.NotNull(result.RejectReason);
        Assert.Contains("exceeds max", result.RejectReason);
    }

    [Fact]
    public void ValidateExecution_SellWithNoSlippage_IsAcceptable()
    {
        // Arrange
        var validator = new ExecutionValidator(maxSlippagePercent: 1.0m);
        decimal expectedPrice = 45000m;
        decimal actualPrice = 45000m;

        // Act
        var result = validator.ValidateExecution(expectedPrice, actualPrice, OrderSide.Sell);

        // Assert
        Assert.True(result.IsAcceptable);
        Assert.Equal(0m, result.SlippagePercent);
    }

    [Fact]
    public void ValidateExecution_SellWithAcceptableSlippage_IsAcceptable()
    {
        // Arrange
        var validator = new ExecutionValidator(maxSlippagePercent: 1.0m);
        decimal expectedPrice = 45000m;
        decimal actualPrice = 44550m; // 1% slippage

        // Act
        var result = validator.ValidateExecution(expectedPrice, actualPrice, OrderSide.Sell);

        // Assert
        Assert.True(result.IsAcceptable);
        Assert.Equal(1m, result.SlippagePercent, precision: 1);
    }

    [Fact]
    public void ValidateExecution_StoresExpectedAndActualPrices()
    {
        // Arrange
        var validator = new ExecutionValidator();
        decimal expectedPrice = 45000m;
        decimal actualPrice = 45450m;

        // Act
        var result = validator.ValidateExecution(expectedPrice, actualPrice, OrderSide.Buy);

        // Assert
        Assert.Equal(expectedPrice, result.ExpectedPrice);
        Assert.Equal(actualPrice, result.ActualPrice);
    }

    [Theory]
    [InlineData(45000, 45450, 1.0, true)]   // Exactly at threshold
    [InlineData(45000, 45900, 2.0, true)]   // Within 2% threshold
    [InlineData(45000, 46350, 2.8, false)]  // Exceeds 2% threshold
    public void ValidateExecution_WithVariousSlippageThresholds(
        decimal expectedPrice,
        decimal actualPrice,
        decimal maxSlippagePercent,
        bool shouldBeAcceptable)
    {
        // Arrange
        var validator = new ExecutionValidator(maxSlippagePercent);

        // Act
        var result = validator.ValidateExecution(expectedPrice, actualPrice, OrderSide.Buy);

        // Assert
        Assert.Equal(shouldBeAcceptable, result.IsAcceptable);
    }
}
