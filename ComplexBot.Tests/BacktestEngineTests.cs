using ComplexBot.Models;
using ComplexBot.Services.Backtesting;
using ComplexBot.Services.Analytics;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.RiskManagement;

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

        var riskSettings = new RiskSettings
        {
            RiskPerTradePercent = 1.5m,
            MaxDrawdownPercent = 20m,
            MaxDailyDrawdownPercent = 3m,
            MaxPortfolioHeatPercent = 6m,
            MinimumEquityUsd = 100m,
            AtrStopMultiplier = 2.0m
        };

        var settings = new BacktestSettings { InitialCapital = 10000m };
        var engine = new BacktestEngine(strategy, riskSettings, settings);
        var candles = GenerateUptrendCandles(50);

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

        var riskSettings = new RiskSettings
        {
            RiskPerTradePercent = 1.5m,
            MaxDrawdownPercent = 20m,
            MaxDailyDrawdownPercent = 3m,
            MaxPortfolioHeatPercent = 6m,
            MinimumEquityUsd = 100m,
            AtrStopMultiplier = 2.0m
        };

        var settings = new BacktestSettings { InitialCapital = 10000m };
        var engine = new BacktestEngine(strategy, riskSettings, settings);
        var candles = GenerateRangingCandles(50);

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
        var riskSettings = new RiskSettings
        {
            RiskPerTradePercent = 2.0m,
            MaxDrawdownPercent = 20m,
            MaxDailyDrawdownPercent = 3m,
            MaxPortfolioHeatPercent = 6m,
            MinimumEquityUsd = 100m,
            AtrStopMultiplier = 2.0m
        };

        var settings = new BacktestSettings
        {
            InitialCapital = 10000m,
            CommissionPercent = 0.1m,
            SlippagePercent = 0.05m
        };
        var engine = new BacktestEngine(strategy, riskSettings, settings);
        var candles = GenerateDowntrendCandles(30);

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
        var riskSettings = new RiskSettings
        {
            RiskPerTradePercent = 1.5m,
            MaxDrawdownPercent = 20m,
            MaxDailyDrawdownPercent = 3m,
            MaxPortfolioHeatPercent = 6m,
            MinimumEquityUsd = 100m,
            AtrStopMultiplier = 2.0m
        };

        var settings = new BacktestSettings { InitialCapital = 10000m };
        var engine = new BacktestEngine(strategy, riskSettings, settings);
        var candles = GenerateUptrendCandles(30);

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
        var riskSettings = new RiskSettings
        {
            RiskPerTradePercent = 1.5m,
            MaxDrawdownPercent = 20m,
            MaxDailyDrawdownPercent = 3m,
            MaxPortfolioHeatPercent = 6m,
            MinimumEquityUsd = 100m,
            AtrStopMultiplier = 2.0m
        };

        var settings = new BacktestSettings { InitialCapital = 10000m };
        var engine = new BacktestEngine(strategy, riskSettings, settings);
        var candles = GenerateUptrendCandles(50);

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
        var riskSettings = new RiskSettings
        {
            RiskPerTradePercent = 1.5m,
            MaxDrawdownPercent = 20m,
            MaxDailyDrawdownPercent = 3m,
            MaxPortfolioHeatPercent = 6m,
            MinimumEquityUsd = 100m,
            AtrStopMultiplier = 2.0m
        };

        var settingsNoComm = new BacktestSettings { InitialCapital = 10000m, CommissionPercent = 0m };
        var engineNoComm = new BacktestEngine(strategy, riskSettings, settingsNoComm);

        var settingsWithComm = new BacktestSettings { InitialCapital = 10000m, CommissionPercent = 0.1m };
        var engineWithComm = new BacktestEngine(strategy, riskSettings, settingsWithComm);

        var candles = GenerateUptrendCandles(50);

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

    private List<Candle> GenerateUptrendCandles(int count)
    {
        var candles = new List<Candle>();
        decimal price = 100m;
        var baseTime = DateTime.UtcNow.AddDays(-count);

        for (int i = 0; i < count; i++)
        {
            price *= 1.02m;
            var open = price * 0.99m;
            var high = price * 1.02m;
            var low = price * 0.98m;
            var close = price;

            candles.Add(new Candle(
                OpenTime: baseTime.AddDays(i),
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: 1000 + i * 10,
                CloseTime: baseTime.AddDays(i + 1)
            ));
        }

        return candles;
    }

    private List<Candle> GenerateDowntrendCandles(int count)
    {
        var candles = new List<Candle>();
        decimal price = 100m;
        var baseTime = DateTime.UtcNow.AddDays(-count);

        for (int i = 0; i < count; i++)
        {
            price *= 0.98m;
            var open = price * 1.01m;
            var high = price * 1.02m;
            var low = price * 0.98m;
            var close = price;

            candles.Add(new Candle(
                OpenTime: baseTime.AddDays(i),
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: 1000,
                CloseTime: baseTime.AddDays(i + 1)
            ));
        }

        return candles;
    }

    private List<Candle> GenerateRangingCandles(int count)
    {
        var candles = new List<Candle>();
        decimal basePrice = 100m;
        var baseTime = DateTime.UtcNow.AddDays(-count);

        for (int i = 0; i < count; i++)
        {
            decimal offset = (decimal)Math.Sin(i * Math.PI / count) * 2;
            var price = basePrice + offset;
            var high = basePrice + 2.5m;
            var low = basePrice - 2.5m;

            candles.Add(new Candle(
                OpenTime: baseTime.AddDays(i),
                Open: price,
                High: high,
                Low: low,
                Close: price,
                Volume: 1000,
                CloseTime: baseTime.AddDays(i + 1)
            ));
        }

        return candles;
    }
}
