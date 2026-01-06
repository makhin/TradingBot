using ComplexBot.Models;
using ComplexBot.Services.Strategies;

namespace ComplexBot.Tests;

public class RsiStrategyTests
{
    [Fact]
    public void Analyze_WithOversoldRecovery_SetsStopLossAndStopsOut()
    {
        var settings = new RsiStrategySettings
        {
            RsiPeriod = 2,
            OversoldLevel = 40m,
            OverboughtLevel = 60m,
            NeutralZoneLow = 45m,
            NeutralZoneHigh = 55m,
            ExitOnNeutral = false,
            AtrPeriod = 2,
            AtrStopMultiplier = 1.0m,
            TakeProfitMultiplier = 1.0m,
            TrendFilterPeriod = 3,
            UseTrendFilter = false,
            VolumePeriod = 2,
            VolumeThreshold = 1.0m,
            RequireVolumeConfirmation = false
        };
        var strategy = new RsiStrategy(settings);
        var candles = TestDataFactory.BuildOversoldRecovery();

        TradeSignal? entrySignal = null;
        foreach (var candle in candles)
        {
            var signal = strategy.Analyze(candle, currentPosition: null, symbol: "BTCUSDT");
            if (signal?.Type == SignalType.Buy)
            {
                entrySignal = signal;
                break;
            }
        }

        Assert.NotNull(entrySignal);
        Assert.NotNull(strategy.CurrentStopLoss);

        var stopLoss = strategy.CurrentStopLoss!.Value;
        var exitCandle = TestDataFactory.CreateCandle(
            TestDataFactory.BaseTime.AddMinutes(10),
            stopLoss + 0.5m,
            high: stopLoss + 1.0m,
            low: stopLoss - 1.0m
        );
        var exitSignal = strategy.Analyze(exitCandle, currentPosition: 1m, symbol: "BTCUSDT");

        Assert.NotNull(exitSignal);
        Assert.Equal(SignalType.Exit, exitSignal!.Type);
    }

}
