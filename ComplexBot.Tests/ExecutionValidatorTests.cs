using Binance.Net.Enums;
using TradingBot.Binance.Common;

namespace ComplexBot.Tests;

public class ExecutionValidatorTests
{
    [Fact]
    public void ValidateExecution_Buy_PositiveSlippageMarkedWorse()
    {
        var validator = new ExecutionValidator(maxSlippagePercent: 5m);

        var result = validator.ValidateExecution(expectedPrice: 100m, actualPrice: 101m, OrderSide.Buy);

        Assert.Equal(1m, result.SlippagePercent);
        var description = validator.GetSlippageDescription(result, OrderSide.Buy);
        Assert.Contains("worse", description);
    }

    [Fact]
    public void ValidateExecution_Sell_PositiveSlippageMarkedBetter()
    {
        var validator = new ExecutionValidator(maxSlippagePercent: 5m);

        var result = validator.ValidateExecution(expectedPrice: 100m, actualPrice: 99m, OrderSide.Sell);

        Assert.Equal(1m, result.SlippagePercent);
        var description = validator.GetSlippageDescription(result, OrderSide.Sell);
        Assert.Contains("better", description);
    }
}
