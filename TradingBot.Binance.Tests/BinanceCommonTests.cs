using Binance.Net.Enums;
using TradingBot.Binance.Common;
using TradingBot.Binance.Common.Models;

namespace TradingBot.Binance.Tests;

public class BinanceCommonIntegrationTests
{
    [Fact]
    public void ExecutionValidator_IntegrationWithRealScenarios()
    {
        // Arrange
        var validator = new ExecutionValidator(maxSlippagePercent: 0.5m);

        // Act - Simulate various trading scenarios
        var scenario1 = validator.ValidateExecution(100m, 100.25m, OrderSide.Buy);
        var scenario2 = validator.ValidateExecution(100m, 99.75m, OrderSide.Sell);
        var scenario3 = validator.ValidateExecution(100m, 101.5m, OrderSide.Buy);

        // Assert
        // 0.25% is within 0.5% threshold - acceptable
        Assert.True(scenario1.IsAcceptable);
        // 0.25% is within 0.5% threshold - acceptable  
        Assert.True(scenario2.IsAcceptable);
        // 1.5% exceeds 0.5% threshold - not acceptable
        Assert.False(scenario3.IsAcceptable);
    }

    [Fact]
    public void ExecutionValidator_CalculatesSlippageAmountCorrectly()
    {
        // Arrange
        var validator = new ExecutionValidator();

        // Act
        var result = validator.ValidateExecution(45000m, 45450m, OrderSide.Buy);

        // Assert
        Assert.Equal(450m, result.SlippageAmount);
    }

    [Fact]
    public void ExecutionValidator_HandlesSmallPrices()
    {
        // Arrange
        var validator = new ExecutionValidator(maxSlippagePercent: 2.0m);

        // Act
        var result = validator.ValidateExecution(0.001m, 0.00102m, OrderSide.Buy);

        // Assert
        Assert.True(result.IsAcceptable);
        Assert.Equal(2m, result.SlippagePercent, precision: 1);
    }

    [Fact]
    public void ExecutionValidator_HandlesLargePrices()
    {
        // Arrange
        var validator = new ExecutionValidator(maxSlippagePercent: 1.0m);

        // Act
        // 1010000 - 1000000 = 10000
        // slippage = (10000 / 1000000) * 100 = 1.0%
        var result = validator.ValidateExecution(1000000m, 1010000m, OrderSide.Buy);

        // Assert
        // 1% slippage is exactly at threshold, should be acceptable
        Assert.True(result.IsAcceptable);
    }
}
