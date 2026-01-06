using ComplexBot.Models;

namespace ComplexBot.Tests;

public class ModelValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Trade_Create_Throws_WhenEntryPriceNotPositive(decimal entryPrice)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Trade.Create(
                "BTCUSDT",
                DateTime.UtcNow,
                null,
                entryPrice,
                null,
                1m,
                TradeDirection.Long,
                null,
                null,
                null));

        Assert.Contains("Entry price", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    public void Trade_Create_Throws_WhenQuantityNotPositive(decimal quantity)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Trade.Create(
                "BTCUSDT",
                DateTime.UtcNow,
                null,
                45000m,
                null,
                quantity,
                TradeDirection.Long,
                null,
                null,
                null));

        Assert.Contains("Quantity", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TradeSignal_Create_Throws_WhenPriceNotPositive(decimal price)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TradeSignal.Create(
                "BTCUSDT",
                SignalType.Buy,
                price,
                null,
                null,
                "test"));

        Assert.Contains("Price", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    public void TradeSignal_Create_Throws_WhenPartialExitQuantityNotPositive(decimal quantity)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TradeSignal.Create(
                "BTCUSDT",
                SignalType.Sell,
                45000m,
                null,
                null,
                "test",
                PartialExitQuantity: quantity));

        Assert.Contains("Partial exit quantity", exception.Message);
    }
}
