using TradingBot.Core.RiskManagement;

namespace TradingBot.Core.Tests;

public class EquityTrackerTests
{
    [Fact]
    public void Constructor_SetsInitialCapitalAsCurrentEquity()
    {
        // Arrange
        decimal initialCapital = 10000m;

        // Act
        var tracker = new EquityTracker(initialCapital);

        // Assert
        Assert.Equal(initialCapital, tracker.CurrentEquity);
        Assert.Equal(initialCapital, tracker.PeakEquity);
    }

    [Fact]
    public void Update_IncreasesCurrentEquity()
    {
        // Arrange
        var tracker = new EquityTracker(10000m);
        decimal newEquity = 11000m;

        // Act
        tracker.Update(newEquity);

        // Assert
        Assert.Equal(newEquity, tracker.CurrentEquity);
        Assert.Equal(newEquity, tracker.PeakEquity); // Peak should also increase
    }

    [Fact]
    public void Update_DecreasesCurrentEquity()
    {
        // Arrange
        var tracker = new EquityTracker(10000m);
        decimal newEquity = 9000m;

        // Act
        tracker.Update(newEquity);

        // Assert
        Assert.Equal(newEquity, tracker.CurrentEquity);
        Assert.Equal(10000m, tracker.PeakEquity); // Peak should remain at initial
    }

    [Fact]
    public void DrawdownAbsolute_CalculatesCorrectly()
    {
        // Arrange
        var tracker = new EquityTracker(10000m);
        tracker.Update(11000m); // Peak = 11000
        tracker.Update(10100m);

        // Act
        decimal drawdown = tracker.DrawdownAbsolute;

        // Assert
        Assert.Equal(900m, drawdown);
    }

    [Fact]
    public void DrawdownPercent_CalculatesCorrectly()
    {
        // Arrange
        var tracker = new EquityTracker(10000m);
        tracker.Update(10000m);
        tracker.Update(9000m);

        // Act
        decimal drawdownPercent = tracker.DrawdownPercent;

        // Assert
        Assert.Equal(10m, drawdownPercent, precision: 2);
    }

    [Fact]
    public void DrawdownPercent_ZeroWhenNoDrawdown()
    {
        // Arrange
        var tracker = new EquityTracker(10000m);

        // Act
        decimal drawdownPercent = tracker.DrawdownPercent;

        // Assert
        Assert.Equal(0m, drawdownPercent);
    }

    [Fact]
    public void Add_IncreasesEquityByAmount()
    {
        // Arrange
        var tracker = new EquityTracker(10000m);

        // Act
        tracker.Add(500m);

        // Assert
        Assert.Equal(10500m, tracker.CurrentEquity);
    }

    [Fact]
    public void IsDrawdownExceeded_ReturnsTrueWhenThresholdCrossed()
    {
        // Arrange
        var tracker = new EquityTracker(10000m);
        tracker.Update(9800m); // 2% drawdown

        // Act
        bool exceeded = tracker.IsDrawdownExceeded(1m);

        // Assert
        Assert.True(exceeded);
    }
}
