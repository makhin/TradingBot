using ComplexBot.Models;
using ComplexBot.Services.Strategies;

namespace ComplexBot.Tests;

public class MaStrategyTests
{
    [Fact]
    public void Analyze_WithBullishCrossover_UpdatesTrailingStop()
    {
        var settings = new MaStrategySettings
        {
            FastMaPeriod = 2,
            SlowMaPeriod = 3,
            AtrPeriod = 2,
            AtrStopMultiplier = 1.0m,
            TakeProfitMultiplier = 1.0m,
            VolumePeriod = 2,
            VolumeThreshold = 1.0m,
            RequireVolumeConfirmation = false
        };
        var strategy = new MaStrategy(settings);
        var candles = BuildCrossoverCandles();

        TradeSignal? entrySignal = null;
        int entryIndex = -1;
        for (int i = 0; i < candles.Count; i++)
        {
            var signal = strategy.Analyze(candles[i], currentPosition: null, symbol: "BTCUSDT");
            if (signal?.Type == SignalType.Buy)
            {
                entrySignal = signal;
                entryIndex = i;
                break;
            }
        }

        Assert.NotNull(entrySignal);
        Assert.NotNull(strategy.CurrentStopLoss);

        var initialStop = strategy.CurrentStopLoss!.Value;
        var followUpIndex = Math.Min(entryIndex + 1, candles.Count - 1);
        var followUpSignal = strategy.Analyze(candles[followUpIndex], currentPosition: 1m, symbol: "BTCUSDT");

        Assert.Null(followUpSignal);
        Assert.True(strategy.CurrentStopLoss >= initialStop, "Trailing stop should not decrease after favorable move.");
    }

    private static List<Candle> BuildCrossoverCandles()
    {
        var candles = new List<Candle>();
        var baseTime = DateTime.UtcNow;
        var closes = new[] { 100m, 98m, 96m, 101m, 105m, 108m, 110m };

        for (int i = 0; i < closes.Length; i++)
        {
            candles.Add(CreateCandle(baseTime.AddMinutes(i), closes[i]));
        }

        return candles;
    }

    private static Candle CreateCandle(DateTime time, decimal close)
    {
        var open = close * 0.99m;
        var high = close * 1.01m;
        var low = close * 0.98m;
        var volume = 1000m;

        return new Candle(time, open, high, low, close, volume, time.AddMinutes(1));
    }
}
