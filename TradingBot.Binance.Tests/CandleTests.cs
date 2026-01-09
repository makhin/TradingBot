using TradingBot.Core.Models;

namespace TradingBot.Binance.Tests;

public class BinanceModelTests
{
    [Fact]
    public void Candle_ConstructorSetsAllProperties()
    {
        // Arrange
        var openTime = DateTime.UtcNow;
        var closeTime = openTime.AddMinutes(1);
        decimal open = 45000m;
        decimal high = 45500m;
        decimal low = 44500m;
        decimal close = 45200m;
        decimal volume = 100m;

        // Act
        var candle = new Candle(openTime, open, high, low, close, volume, closeTime);

        // Assert
        Assert.Equal(openTime, candle.OpenTime);
        Assert.Equal(open, candle.Open);
        Assert.Equal(high, candle.High);
        Assert.Equal(low, candle.Low);
        Assert.Equal(close, candle.Close);
        Assert.Equal(volume, candle.Volume);
        Assert.Equal(closeTime, candle.CloseTime);
    }

    [Fact]
    public void Candle_HighIsHighestPoint()
    {
        // Arrange
        var openTime = DateTime.UtcNow;
        var closeTime = openTime.AddMinutes(1);

        // Act
        var candle = new Candle(openTime, 45000m, 45500m, 44500m, 45200m, 100m, closeTime);

        // Assert
        Assert.True(candle.High >= candle.Open);
        Assert.True(candle.High >= candle.Close);
        Assert.True(candle.High >= candle.Low);
    }

    [Fact]
    public void Candle_LowIsLowestPoint()
    {
        // Arrange
        var openTime = DateTime.UtcNow;
        var closeTime = openTime.AddMinutes(1);

        // Act
        var candle = new Candle(openTime, 45000m, 45500m, 44500m, 45200m, 100m, closeTime);

        // Assert
        Assert.True(candle.Low <= candle.Open);
        Assert.True(candle.Low <= candle.Close);
        Assert.True(candle.Low <= candle.High);
    }

    [Theory]
    [InlineData(45000, 45500, 44500, 45200, 100)]
    [InlineData(50000, 51000, 49000, 50500, 200)]
    [InlineData(100, 150, 50, 120, 10)]
    public void Candle_WithVariousPrices_CreatesSuccessfully(
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal volume)
    {
        // Arrange
        var openTime = DateTime.UtcNow;
        var closeTime = openTime.AddMinutes(1);

        // Act
        var candle = new Candle(openTime, open, high, low, close, volume, closeTime);

        // Assert
        Assert.NotNull(candle);
        Assert.Equal(open, candle.Open);
        Assert.Equal(high, candle.High);
    }
}
