using ComplexBot.Models;

namespace ComplexBot.Integration;

public static class TestDataFactory
{
    public static readonly DateTime BaseTime = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static List<Candle> GenerateUptrendCandles(int count)
    {
        var candles = new List<Candle>();
        decimal price = 45000m;
        var baseTime = BaseTime.AddDays(-count);
        var random = new Random(42);

        for (int i = 0; i < count; i++)
        {
            price *= (decimal)(1.0 + random.NextDouble() * 0.02);
            var open = price * 0.99m;
            var high = price * 1.015m;
            var low = price * 0.985m;
            var close = price;
            var volume = (decimal)(1000 + random.Next(500));

            candles.Add(new Candle(
                OpenTime: baseTime.AddHours(i * 4),
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: volume,
                CloseTime: baseTime.AddHours(i * 4 + 4)
            ));
        }

        return candles;
    }

    public static List<Candle> GenerateDowntrendCandles(int count)
    {
        var candles = new List<Candle>();
        decimal price = 50000m;
        var baseTime = BaseTime.AddDays(-count);
        var random = new Random(42);

        for (int i = 0; i < count; i++)
        {
            price *= (decimal)(1.0 - random.NextDouble() * 0.015);
            var open = price * 1.01m;
            var high = price * 1.02m;
            var low = price * 0.985m;
            var close = price;
            var volume = (decimal)(1000 + random.Next(500));

            candles.Add(new Candle(
                OpenTime: baseTime.AddHours(i * 4),
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: volume,
                CloseTime: baseTime.AddHours(i * 4 + 4)
            ));
        }

        return candles;
    }

    public static List<Candle> GenerateRangingCandles(int count)
    {
        var candles = new List<Candle>();
        decimal basePrice = 45000m;
        var baseTime = BaseTime.AddDays(-count);
        var random = new Random(42);

        for (int i = 0; i < count; i++)
        {
            decimal offset = (decimal)Math.Sin(i * Math.PI / (count / 3)) * 300;
            var price = basePrice + offset;
            var open = price;
            var high = basePrice + 1000;
            var low = basePrice - 1000;
            var close = price;
            var volume = (decimal)(1000 + random.Next(500));

            candles.Add(new Candle(
                OpenTime: baseTime.AddHours(i * 4),
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: volume,
                CloseTime: baseTime.AddHours(i * 4 + 4)
            ));
        }

        return candles;
    }

    public static List<Candle> GenerateVolumeSpike(int count)
    {
        var candles = new List<Candle>();
        decimal price = 45000m;
        var baseTime = BaseTime.AddDays(-count);
        var random = new Random(42);

        for (int i = 0; i < count; i++)
        {
            price *= 1.01m;
            var open = price * 0.99m;
            var high = price * 1.02m;
            var low = price * 0.98m;
            var close = price;

            var volume = (i % 10 == 0)
                ? (decimal)(5000 + random.Next(2000))
                : (decimal)(1000 + random.Next(500));

            candles.Add(new Candle(
                OpenTime: baseTime.AddHours(i * 4),
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: volume,
                CloseTime: baseTime.AddHours(i * 4 + 4)
            ));
        }

        return candles;
    }

    public static List<Candle> GenerateGapUpMove(int count)
    {
        var candles = new List<Candle>();
        decimal price = 45000m;
        var baseTime = BaseTime.AddDays(-count);

        for (int i = 0; i < count; i++)
        {
            if (i == 0)
            {
                price *= 1.05m;
            }
            else
            {
                price *= 1.01m;
            }

            var open = price * 0.99m;
            var high = price * 1.02m;
            var low = price * 0.98m;
            var close = price;
            var volume = (decimal)(1000 + i * 100);

            candles.Add(new Candle(
                OpenTime: baseTime.AddHours(i * 4),
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: volume,
                CloseTime: baseTime.AddHours(i * 4 + 4)
            ));
        }

        return candles;
    }
}
