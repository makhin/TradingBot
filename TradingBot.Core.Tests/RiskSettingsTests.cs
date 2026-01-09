using TradingBot.Core.RiskManagement;

namespace TradingBot.Core.Tests;

public class RiskSettingsTests
{
    [Fact]
    public void RiskPerTradePercent_Default_IsValid()
    {
        // Arrange & Act
        var settings = new RiskSettings { RiskPerTradePercent = 1.5m };

        // Assert
        Assert.Equal(1.5m, settings.RiskPerTradePercent);
        Assert.True(settings.RiskPerTradePercent > 0);
        Assert.True(settings.RiskPerTradePercent <= 100);
    }

    [Fact]
    public void MaxDailyDrawdownPercent_Default_IsValid()
    {
        // Arrange & Act
        var settings = new RiskSettings { MaxDailyDrawdownPercent = 5m };

        // Assert
        Assert.Equal(5m, settings.MaxDailyDrawdownPercent);
        Assert.True(settings.MaxDailyDrawdownPercent > 0);
    }

    [Fact]
    public void MaxDrawdownPercent_Default_IsValid()
    {
        // Arrange & Act
        var settings = new RiskSettings { MaxDrawdownPercent = 20m };

        // Assert
        Assert.Equal(20m, settings.MaxDrawdownPercent);
        Assert.True(settings.MaxDrawdownPercent > 0);
    }

    [Fact]
    public void Constructor_WithValues_SetsProperties()
    {
        // Arrange
        decimal riskPercent = 1.5m;
        decimal maxDaily = 3m;
        decimal maxDrawdown = 20m;

        // Act
        var settings = new RiskSettings
        {
            RiskPerTradePercent = riskPercent,
            MaxDailyDrawdownPercent = maxDaily,
            MaxDrawdownPercent = maxDrawdown
        };

        // Assert
        Assert.Equal(riskPercent, settings.RiskPerTradePercent);
        Assert.Equal(maxDaily, settings.MaxDailyDrawdownPercent);
        Assert.Equal(maxDrawdown, settings.MaxDrawdownPercent);
    }

    [Fact]
    public void AtrStopMultiplier_HasDefaultValue()
    {
        // Arrange & Act
        var settings = new RiskSettings();

        // Assert
        Assert.Equal(2.5m, settings.AtrStopMultiplier);
    }

    [Fact]
    public void TakeProfitMultiplier_HasDefaultValue()
    {
        // Arrange & Act
        var settings = new RiskSettings();

        // Assert
        Assert.Equal(1.5m, settings.TakeProfitMultiplier);
    }
}
