using ComplexBot.Models;

namespace ComplexBot.Tests;

public static class TestDataFactory
{
    public static readonly DateTime BaseTime = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static Candle CreateCandle(
        DateTime time,
        decimal close,
        decimal? high = null,
        decimal? low = null,
        decimal volume = 1000m,
        TimeSpan? interval = null)
    {
        var open = close * 0.99m;
        var candleHigh = high ?? close * 1.01m;
        var candleLow = low ?? close * 0.98m;
        var closeTime = time.Add(interval ?? TimeSpan.FromMinutes(1));

        return new Candle(time, open, candleHigh, candleLow, close, volume, closeTime);
    }

    public static List<Candle> GenerateUptrendCandles(int count)
    {
        var candles = new List<Candle>();
        decimal price = 100m;
        var baseTime = BaseTime.AddDays(-count);

        for (int i = 0; i < count; i++)
        {
            price *= 1.02m;
            var open = price * 0.99m;
            var high = price * 1.02m;
            var low = price * 0.98m;

            candles.Add(new Candle(
                OpenTime: baseTime.AddDays(i),
                Open: open,
                High: high,
                Low: low,
                Close: price,
                Volume: 1000 + i * 10,
                CloseTime: baseTime.AddDays(i + 1)
            ));
        }

        return candles;
    }

    public static List<Candle> GenerateDowntrendCandles(int count)
    {
        var candles = new List<Candle>();
        decimal price = 100m;
        var baseTime = BaseTime.AddDays(-count);

        for (int i = 0; i < count; i++)
        {
            price *= 0.98m;
            var open = price * 1.01m;
            var high = price * 1.02m;
            var low = price * 0.98m;

            candles.Add(new Candle(
                OpenTime: baseTime.AddDays(i),
                Open: open,
                High: high,
                Low: low,
                Close: price,
                Volume: 1000,
                CloseTime: baseTime.AddDays(i + 1)
            ));
        }

        return candles;
    }

    public static List<Candle> GenerateRangingCandles(int count)
    {
        var candles = new List<Candle>();
        decimal basePrice = 100m;
        var baseTime = BaseTime.AddDays(-count);

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

    public static List<Candle> GenerateStrongUptrend(int count)
    {
        var candles = new List<Candle>();
        decimal price = 100m;
        var baseTime = BaseTime.AddHours(-count);

        for (int i = 0; i < count; i++)
        {
            price *= 1.05m;
            var open = price * 0.97m;
            var high = price * 1.03m;
            var low = price * 0.96m;
            var volume = 2000m + i * 200m;

            candles.Add(new Candle(
                OpenTime: baseTime.AddHours(i),
                Open: open,
                High: high,
                Low: low,
                Close: price,
                Volume: volume,
                CloseTime: baseTime.AddHours(i + 1)
            ));
        }

        return candles;
    }

    public static List<Candle> GenerateBullishSetup(int count)
    {
        var candles = new List<Candle>();
        decimal price = 100m;
        var baseTime = BaseTime.AddHours(-count);

        for (int i = 0; i < count; i++)
        {
            price *= 1.02m;
            var open = price * 0.98m;
            var high = price * 1.02m;
            var low = price * 0.97m;
            var volume = (i % 3 == 0) ? 2000m : 1000m;

            candles.Add(new Candle(
                OpenTime: baseTime.AddHours(i),
                Open: open,
                High: high,
                Low: low,
                Close: price,
                Volume: volume,
                CloseTime: baseTime.AddHours(i + 1)
            ));
        }

        return candles;
    }

    public static List<Candle> GenerateBearishSetup(int count)
    {
        var candles = new List<Candle>();
        decimal price = 120m;
        var baseTime = BaseTime;

        for (int i = 0; i < count; i++)
        {
            price *= 0.97m;
            var open = price * 1.02m;
            var high = price * 1.03m;
            var low = price * 0.98m;

            candles.Add(new Candle(
                OpenTime: baseTime.AddHours(i),
                Open: open,
                High: high,
                Low: low,
                Close: price,
                Volume: 1000,
                CloseTime: baseTime.AddHours(i + 1)
            ));
        }

        return candles;
    }

    public static List<Candle> GenerateBullishSetupLowVolume(int count)
    {
        var candles = new List<Candle>();
        decimal price = 100m;
        var baseTime = BaseTime.AddHours(-count);

        for (int i = 0; i < count; i++)
        {
            price *= 1.02m;
            var open = price * 0.98m;
            var high = price * 1.02m;
            var low = price * 0.97m;

            candles.Add(new Candle(
                OpenTime: baseTime.AddHours(i),
                Open: open,
                High: high,
                Low: low,
                Close: price,
                Volume: 500m,
                CloseTime: baseTime.AddHours(i + 1)
            ));
        }

        return candles;
    }

    public static List<Candle> GenerateRangingMarket(int count)
    {
        var candles = new List<Candle>();
        decimal basePrice = 100m;
        var baseTime = BaseTime.AddHours(-count);

        for (int i = 0; i < count; i++)
        {
            decimal offset = (decimal)Math.Sin(i * Math.PI / count) * 2;
            var price = basePrice + offset;
            var high = basePrice + 2.5m;
            var low = basePrice - 2.5m;

            candles.Add(new Candle(
                OpenTime: baseTime.AddHours(i),
                Open: price,
                High: high,
                Low: low,
                Close: price,
                Volume: 1000,
                CloseTime: baseTime.AddHours(i + 1)
            ));
        }

        return candles;
    }

    public static List<Candle> BuildCrossoverCandles()
    {
        var candles = new List<Candle>();
        var closes = new[] { 100m, 98m, 96m, 101m, 105m, 108m, 110m };

        for (int i = 0; i < closes.Length; i++)
        {
            candles.Add(CreateCandle(BaseTime.AddMinutes(i), closes[i]));
        }

        return candles;
    }

    public static List<Candle> BuildOversoldRecovery()
    {
        var candles = new List<Candle>();
        var closes = new[] { 100m, 92m, 85m, 88m, 92m, 96m };

        for (int i = 0; i < closes.Length; i++)
        {
            candles.Add(CreateCandle(BaseTime.AddMinutes(i), closes[i]));
        }

        return candles;
    }

    public static IEnumerable<object[]> AtrCandleCases()
    {
        yield return new object[]
        {
            CreateCandle(BaseTime, 102m, high: 105m, low: 98m, interval: TimeSpan.FromHours(1)),
            CreateCandle(BaseTime.AddHours(1), 107m, high: 108m, low: 101m, interval: TimeSpan.FromHours(1))
        };

        yield return new object[]
        {
            CreateCandle(BaseTime, 102m, high: 105m, low: 98m, interval: TimeSpan.FromHours(1)),
            CreateCandle(BaseTime.AddHours(1), 104m, high: 106m, low: 99m, interval: TimeSpan.FromHours(1))
        };
    }
}
