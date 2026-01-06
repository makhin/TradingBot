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
        var candles = BuildOversoldRecovery();

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
        var exitCandle = CreateCandle(DateTime.UtcNow.AddMinutes(10), stopLoss + 0.5m, stopLoss + 1.0m, stopLoss - 1.0m);
        var exitSignal = strategy.Analyze(exitCandle, currentPosition: 1m, symbol: "BTCUSDT");

        Assert.NotNull(exitSignal);
        Assert.Equal(SignalType.Exit, exitSignal!.Type);
    }

    private static List<Candle> BuildOversoldRecovery()
    {
        var candles = new List<Candle>();
        var baseTime = DateTime.UtcNow;
        var closes = new[] { 100m, 92m, 85m, 88m, 92m, 96m };

        for (int i = 0; i < closes.Length; i++)
        {
            candles.Add(CreateCandle(baseTime.AddMinutes(i), closes[i]));
        }

        return candles;
    }

    private static Candle CreateCandle(DateTime time, decimal close, decimal? high = null, decimal? low = null)
    {
        var open = close * 0.99m;
        var candleHigh = high ?? close * 1.01m;
        var candleLow = low ?? close * 0.98m;
        var volume = 1000m;

        return new Candle(time, open, candleHigh, candleLow, close, volume, time.AddMinutes(1));
    }
}
