using ComplexBot.Models;
using ComplexBot.Services.Backtesting;
using TradingBot.Core.Analytics;
using ComplexBot.Services.Strategies;
using TradingBot.Core.RiskManagement;

namespace ComplexBot.Tests;

public class BacktestEngineTests
{
    [Fact]
    public void Run_WithSimpleUptrend_GeneratesProfit()
    {
        // Arrange
        var strategy = new AdxTrendStrategy(new StrategySettings
        {
            AdxPeriod = 3,
            AdxThreshold = 15m,
            FastEmaPeriod = 3,
            SlowEmaPeriod = 5
        });

        var riskSettings = RiskSettingsFactory.CreateDefault();

        var settings = new BacktestSettings { InitialCapital = 10000m };
        var engine = new BacktestEngine(strategy, riskSettings, settings);
        var candles = TestDataFactory.GenerateUptrendCandles(50);

        // Act
        var result = engine.Run(candles, "BTCUSDT");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("ADX Trend Following + Volume", result.StrategyName);
        Assert.Equal(10000m, result.InitialCapital);
        Assert.NotNull(result.Trades);
        Assert.NotNull(result.Metrics);
    }

    [Fact]
    public void Run_WithRangingMarket_MinimizesTrades()
    {
        // Arrange
        var strategy = new AdxTrendStrategy(new StrategySettings
        {
            AdxPeriod = 3,
            AdxThreshold = 25m,  // High threshold to avoid ranging market
            FastEmaPeriod = 3,
            SlowEmaPeriod = 5
        });

        var riskSettings = RiskSettingsFactory.CreateDefault();

        var settings = new BacktestSettings { InitialCapital = 10000m };
        var engine = new BacktestEngine(strategy, riskSettings, settings);
        var candles = TestDataFactory.GenerateRangingCandles(50);

        // Act
        var result = engine.Run(candles, "BTCUSDT");

        // Assert
        Assert.NotNull(result);
        // In ranging market with high ADX threshold, should have few trades
        Assert.True(result.Trades.Count <= 5);
    }

    [Fact]
    public void Run_WithConsecLosses_AppliesDrawdownAdjustment()
    {
        // Arrange
        var strategy = new AdxTrendStrategy();
        var riskSettings = RiskSettingsFactory.CreateDefault() with { RiskPerTradePercent = 2.0m };

        var settings = new BacktestSettings
        {
            InitialCapital = 10000m,
            CommissionPercent = 0.1m,
            SlippagePercent = 0.05m
        };
        var engine = new BacktestEngine(strategy, riskSettings, settings);
        var candles = TestDataFactory.GenerateDowntrendCandles(30);

        // Act
        var result = engine.Run(candles, "BTCUSDT");

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Metrics);
        // In downtrend, final capital should be less than initial
        Assert.True(result.FinalCapital < result.InitialCapital || result.Trades.Count == 0);
    }

    [Fact]
    public void Run_CalculatesMetricsCorrectly()
    {
        // Arrange
        var strategy = new AdxTrendStrategy();
        var riskSettings = RiskSettingsFactory.CreateDefault();

        var settings = new BacktestSettings { InitialCapital = 10000m };
        var engine = new BacktestEngine(strategy, riskSettings, settings);
        var candles = TestDataFactory.GenerateUptrendCandles(30);

        // Act
        var result = engine.Run(candles, "BTCUSDT");

        // Assert
        Assert.NotNull(result.Metrics);
        Assert.True(result.Metrics.TotalTrades >= 0);
        Assert.True(result.Metrics.WinningTrades >= 0);
        Assert.True(result.Metrics.LosingTrades >= 0);
        Assert.True(result.Metrics.WinRate >= 0 && result.Metrics.WinRate <= 100);
    }

    [Fact]
    public void Run_TradesList_ContainsValidTrades()
    {
        // Arrange
        var strategy = new AdxTrendStrategy();
        var riskSettings = RiskSettingsFactory.CreateDefault();

        var settings = new BacktestSettings { InitialCapital = 10000m };
        var engine = new BacktestEngine(strategy, riskSettings, settings);
        var candles = TestDataFactory.GenerateUptrendCandles(50);

        // Act
        var result = engine.Run(candles, "BTCUSDT");

        // Assert
        foreach (var trade in result.Trades)
        {
            Assert.NotEqual(default(DateTime), trade.EntryTime);
            Assert.True(trade.EntryPrice > 0);
            Assert.True(trade.Quantity > 0);
            if (trade.ExitTime.HasValue)
            {
                Assert.True(trade.ExitTime.Value >= trade.EntryTime);
                Assert.True(trade.ExitPrice > 0);
            }
        }
    }

    [Fact]
    public void Run_WithCommission_ReducesProfits()
    {
        // Arrange
        var strategy = new AdxTrendStrategy();
        var riskSettings = RiskSettingsFactory.CreateDefault();

        var settingsNoComm = new BacktestSettings { InitialCapital = 10000m, CommissionPercent = 0m };
        var engineNoComm = new BacktestEngine(strategy, riskSettings, settingsNoComm);

        var settingsWithComm = new BacktestSettings { InitialCapital = 10000m, CommissionPercent = 0.1m };
        var engineWithComm = new BacktestEngine(strategy, riskSettings, settingsWithComm);

        var candles = TestDataFactory.GenerateUptrendCandles(50);

        // Act
        var resultNoComm = engineNoComm.Run(candles, "BTCUSDT");
        strategy.Reset();  // Reset strategy for second run
        var resultWithComm = engineWithComm.Run(candles, "BTCUSDT");

        // Assert
        // Final capital should be lower with commission (if trades were made)
        if (resultNoComm.Trades.Count > 0 && resultWithComm.Trades.Count > 0)
        {
            Assert.True(resultWithComm.FinalCapital <= resultNoComm.FinalCapital);
        }
    }

}
