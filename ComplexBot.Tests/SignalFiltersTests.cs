using ComplexBot.Models;
using ComplexBot.Services.Trading;
using ComplexBot.Services.Trading.SignalFilters;

namespace ComplexBot.Tests;

public class SignalFiltersTests
{
    private static StrategyState CreateRsiState(decimal rsiValue, bool isOverbought = false, bool isOversold = false)
    {
        return new StrategyState(
            LastSignal: null,
            IndicatorValue: rsiValue,
            IsOverbought: isOverbought,
            IsOversold: isOversold,
            IsTrending: false,
            CustomValues: new Dictionary<string, decimal>()
        );
    }

    private static StrategyState CreateAdxState(decimal adxValue, bool isTrending = false)
    {
        return new StrategyState(
            LastSignal: null,
            IndicatorValue: adxValue,
            IsOverbought: false,
            IsOversold: false,
            IsTrending: isTrending,
            CustomValues: new Dictionary<string, decimal>()
        );
    }

    private static StrategyState CreateTrendState(SignalType? lastSignal, bool isTrending)
    {
        return new StrategyState(
            LastSignal: lastSignal,
            IndicatorValue: null,
            IsOverbought: false,
            IsOversold: false,
            IsTrending: isTrending,
            CustomValues: new Dictionary<string, decimal>()
        );
    }

    #region RSI Filter Tests

    [Fact]
    public void RsiFilter_Confirm_ApprovesBuyInOversold()
    {
        // Arrange
        var filter = new RsiSignalFilter(70m, 30m, FilterMode.Confirm);
        var buySignal = new TradeSignal("BTCUSDT", SignalType.Buy, 45000m, 44000m, 46000m, "Buy signal");
        var rsiState = CreateRsiState(25m, isOversold: true);

        // Act
        var result = filter.Evaluate(buySignal, rsiState);

        // Assert
        Assert.True(result.Approved);
        Assert.Contains("oversold", result.Reason.ToLower());
    }

    [Fact]
    public void RsiFilter_Confirm_RejectsBuyInOverbought()
    {
        // Arrange
        var filter = new RsiSignalFilter(70m, 30m, FilterMode.Confirm);
        var buySignal = new TradeSignal("BTCUSDT", SignalType.Buy, 45000m, 44000m, 46000m, "Buy signal");
        var rsiState = CreateRsiState(75m, isOverbought: true);

        // Act
        var result = filter.Evaluate(buySignal, rsiState);

        // Assert
        Assert.False(result.Approved);
    }

    [Fact]
    public void RsiFilter_Veto_AllowsBuyInNeutral()
    {
        // Arrange
        var filter = new RsiSignalFilter(70m, 30m, FilterMode.Veto);
        var buySignal = new TradeSignal("BTCUSDT", SignalType.Buy, 45000m, 44000m, 46000m, "Buy signal");
        var rsiState = CreateRsiState(50m);

        // Act
        var result = filter.Evaluate(buySignal, rsiState);

        // Assert
        Assert.True(result.Approved);
    }

    [Fact]
    public void RsiFilter_Score_AdjustsConfidenceBasedOnRsi()
    {
        // Arrange
        var filter = new RsiSignalFilter(70m, 30m, FilterMode.Score);
        var buySignal = new TradeSignal("BTCUSDT", SignalType.Buy, 45000m, 44000m, 46000m, "Buy signal");

        // RSI <= 30 returns 1.2 (oversold boost)
        var oversoldState = CreateRsiState(25m, isOversold: true);
        // RSI in neutral zone (30-70) returns 0.5-1.0 scaled
        var neutralState = CreateRsiState(50m);

        // Act
        var oversoldResult = filter.Evaluate(buySignal, oversoldState);
        var neutralResult = filter.Evaluate(buySignal, neutralState);

        // Assert
        Assert.True(oversoldResult.Approved);
        Assert.True(neutralResult.Approved);
        Assert.True(
            oversoldResult.ConfidenceAdjustment > neutralResult.ConfidenceAdjustment,
            "Oversold should give higher confidence than neutral"
        );
    }

    [Fact]
    public void RsiFilter_ExitSignals_NotFiltered()
    {
        // Arrange
        var filter = new RsiSignalFilter(70m, 30m, FilterMode.Confirm);
        var exitSignal = new TradeSignal("BTCUSDT", SignalType.Exit, 45000m, 44000m, 46000m, "Exit");
        var rsiState = CreateRsiState(75m, isOverbought: true);

        // Act
        var result = filter.Evaluate(exitSignal, rsiState);

        // Assert
        Assert.True(result.Approved);
        Assert.Contains("not filtered", result.Reason.ToLower());
    }

    #endregion

    #region ADX Filter Tests

    [Fact]
    public void AdxFilter_Confirm_ApprovesBuyInStrongTrend()
    {
        // Arrange
        var filter = new AdxSignalFilter(20m, 30m, FilterMode.Confirm);
        var buySignal = new TradeSignal("BTCUSDT", SignalType.Buy, 45000m, 44000m, 46000m, "Buy");
        var strongTrendState = CreateAdxState(35m, isTrending: true);

        // Act
        var result = filter.Evaluate(buySignal, strongTrendState);

        // Assert
        Assert.True(result.Approved);
        Assert.Contains("strong trend", result.Reason.ToLower());
    }

    [Fact]
    public void AdxFilter_Confirm_RejectsBuyInWeakTrend()
    {
        // Arrange
        var filter = new AdxSignalFilter(20m, 30m, FilterMode.Confirm);
        var buySignal = new TradeSignal("BTCUSDT", SignalType.Buy, 45000m, 44000m, 46000m, "Buy");
        var weakTrendState = CreateAdxState(15m);

        // Act
        var result = filter.Evaluate(buySignal, weakTrendState);

        // Assert
        Assert.False(result.Approved);
        Assert.Contains("weak", result.Reason.ToLower());
    }

    [Fact]
    public void AdxFilter_Score_HigherConfidenceForStrongerTrends()
    {
        // Arrange
        var filter = new AdxSignalFilter(20m, 30m, FilterMode.Score);
        var buySignal = new TradeSignal("BTCUSDT", SignalType.Buy, 45000m, 44000m, 46000m, "Buy");

        var moderateState = CreateAdxState(25m, isTrending: true);
        var strongState = CreateAdxState(40m, isTrending: true);

        // Act
        var moderateResult = filter.Evaluate(buySignal, moderateState);
        var strongResult = filter.Evaluate(buySignal, strongState);

        // Assert
        Assert.True(moderateResult.Approved);
        Assert.True(strongResult.Approved);
        Assert.True(
            strongResult.ConfidenceAdjustment > moderateResult.ConfidenceAdjustment,
            "Stronger trend should give higher confidence"
        );
    }

    [Fact]
    public void AdxFilter_NoAdxValue_HandledGracefully()
    {
        // Arrange
        var filter = new AdxSignalFilter(20m, 30m, FilterMode.Confirm);
        var buySignal = new TradeSignal("BTCUSDT", SignalType.Buy, 45000m, 44000m, 46000m, "Buy");
        var emptyState = StrategyState.Empty;

        // Act
        var result = filter.Evaluate(buySignal, emptyState);

        // Assert
        Assert.False(result.Approved);
        Assert.Contains("adx value", result.Reason.ToLower());
    }

    #endregion

    #region TrendAlignment Filter Tests

    [Fact]
    public void TrendAlignmentFilter_Confirm_ApprovesBuyWhenAligned()
    {
        // Arrange
        var filter = new TrendAlignmentFilter(FilterMode.Confirm, requireStrictAlignment: true);
        var buySignal = new TradeSignal("BTCUSDT", SignalType.Buy, 45000m, 44000m, 46000m, "Buy");
        var bullishState = CreateTrendState(SignalType.Buy, isTrending: true);

        // Act
        var result = filter.Evaluate(buySignal, bullishState);

        // Assert
        Assert.True(result.Approved);
    }

    [Fact]
    public void TrendAlignmentFilter_Confirm_RejectsBuyWhenMisaligned()
    {
        // Arrange
        var filter = new TrendAlignmentFilter(FilterMode.Confirm, requireStrictAlignment: true);
        var buySignal = new TradeSignal("BTCUSDT", SignalType.Buy, 45000m, 44000m, 46000m, "Buy");
        var bearishState = CreateTrendState(SignalType.Sell, isTrending: true);

        // Act
        var result = filter.Evaluate(buySignal, bearishState);

        // Assert
        Assert.False(result.Approved);
        Assert.Contains("misaligned", result.Reason.ToLower());
    }

    [Fact]
    public void TrendAlignmentFilter_ExitSignals_AlwaysApproved()
    {
        // Arrange
        var filter = new TrendAlignmentFilter(FilterMode.Confirm, requireStrictAlignment: true);
        var exitSignal = new TradeSignal("BTCUSDT", SignalType.Exit, 45000m, 44000m, 46000m, "Exit");
        var anyState = CreateTrendState(SignalType.Sell, isTrending: true);

        // Act
        var result = filter.Evaluate(exitSignal, anyState);

        // Assert
        Assert.True(result.Approved);
    }

    #endregion

    #region Filter Mode Tests

    [Theory]
    [InlineData(FilterMode.Confirm)]
    [InlineData(FilterMode.Veto)]
    [InlineData(FilterMode.Score)]
    public void AllFilters_HandlePartialExitSignals(FilterMode mode)
    {
        // Arrange
        var rsiFilter = new RsiSignalFilter(70m, 30m, mode);
        var adxFilter = new AdxSignalFilter(20m, 30m, mode);
        var trendFilter = new TrendAlignmentFilter(mode, false);

        var partialExitSignal = new TradeSignal("BTCUSDT", SignalType.PartialExit, 45000m, 44000m, 46000m, "Partial");
        var anyState = CreateRsiState(50m);

        // Act
        var rsiResult = rsiFilter.Evaluate(partialExitSignal, anyState);
        var adxResult = adxFilter.Evaluate(partialExitSignal, anyState);
        var trendResult = trendFilter.Evaluate(partialExitSignal, anyState);

        // Assert
        Assert.True(rsiResult.Approved, $"RSI ({mode}) should approve partial exit");
        Assert.True(adxResult.Approved, $"ADX ({mode}) should approve partial exit");
        Assert.True(trendResult.Approved, $"Trend ({mode}) should approve partial exit");
    }

    [Fact]
    public void ScoreMode_ReturnsConfidenceAdjustment()
    {
        // Arrange
        var filter = new RsiSignalFilter(70m, 30m, FilterMode.Score);
        var buySignal = new TradeSignal("BTCUSDT", SignalType.Buy, 45000m, 44000m, 46000m, "Buy");
        var state = CreateRsiState(25m, isOversold: true);

        // Act
        var result = filter.Evaluate(buySignal, state);

        // Assert
        Assert.NotNull(result.ConfidenceAdjustment);
        Assert.True(result.ConfidenceAdjustment > 0, "Score mode should return positive confidence");
    }

    #endregion
}
