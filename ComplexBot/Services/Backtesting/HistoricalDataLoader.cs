using Binance.Net.Clients;
using Binance.Net.Enums;
using CryptoExchange.Net.Authentication;
using ComplexBot.Models;

namespace ComplexBot.Services.Backtesting;

public class HistoricalDataLoader
{
    private readonly BinanceRestClient _client;
    private const string DefaultDataDirectory = "Data";
    
    public HistoricalDataLoader(string? apiKey = null, string? apiSecret = null)
    {
        _client = new BinanceRestClient(options =>
        {
            if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(apiSecret))
            {
                options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
            }
        });
    }

    /// <summary>
    /// Load historical klines from Binance
    /// </summary>
    public async Task<List<Candle>> LoadAsync(
        string symbol,
        KlineInterval interval,
        DateTime startTime,
        DateTime endTime,
        IProgress<int>? progress = null)
    {
        var candles = new List<Candle>();
        var currentStart = startTime;
        int totalDays = (int)(endTime - startTime).TotalDays;
        int processedDays = 0;

        while (currentStart < endTime)
        {
            var result = await _client.SpotApi.ExchangeData.GetKlinesAsync(
                symbol,
                interval,
                currentStart,
                endTime,
                limit: 1000
            );

            if (!result.Success)
            {
                throw new Exception($"Failed to load data: {result.Error?.Message}");
            }

            if (!result.Data.Any())
                break;

            foreach (var kline in result.Data)
            {
                candles.Add(new Candle(
                    kline.OpenTime,
                    kline.OpenPrice,
                    kline.HighPrice,
                    kline.LowPrice,
                    kline.ClosePrice,
                    kline.Volume,
                    kline.CloseTime
                ));
            }

            currentStart = result.Data.Last().CloseTime.AddMilliseconds(1);
            
            processedDays = (int)(currentStart - startTime).TotalDays;
            progress?.Report(totalDays > 0 ? processedDays * 100 / totalDays : 100);

            // Rate limiting
            await Task.Delay(100);
        }

        progress?.Report(100);
        return candles.OrderBy(c => c.OpenTime).ToList();
    }

    /// <summary>
    /// Load candles from CSV cache if available; otherwise download and persist.
    /// </summary>
    public async Task<List<Candle>> LoadFromDiskOrDownloadAsync(
        string symbol,
        KlineInterval interval,
        DateTime startTime,
        DateTime endTime,
        string? dataDirectory = null,
        IProgress<int>? progress = null)
    {
        var directory = string.IsNullOrWhiteSpace(dataDirectory) ? DefaultDataDirectory : dataDirectory;
        var cached = FindCachedFile(directory, symbol, interval, startTime, endTime);

        if (cached != null)
        {
            var cachedCandles = await LoadFromCsvAsync(cached.Path);
            Console.WriteLine($"[Data] Using cached CSV: {cached.Path}");
            return FilterCandlesByRange(cachedCandles, startTime, endTime);
        }

        var candles = await LoadAsync(symbol, interval, startTime, endTime, progress);
        Directory.CreateDirectory(directory);
        var filePath = BuildDataFilePath(directory, symbol, interval, startTime, endTime);
        await SaveToCsvAsync(candles, filePath);
        Console.WriteLine($"[Data] Saved CSV: {filePath}");
        return FilterCandlesByRange(candles, startTime, endTime);
    }

    /// <summary>
    /// Save candles to CSV file
    /// </summary>
    public async Task SaveToCsvAsync(List<Candle> candles, string filePath)
    {
        using var writer = new StreamWriter(filePath);
        await writer.WriteLineAsync("OpenTime,Open,High,Low,Close,Volume,CloseTime");

        foreach (var candle in candles)
        {
            await writer.WriteLineAsync(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss},{1},{2},{3},{4},{5},{6:yyyy-MM-dd HH:mm:ss}",
                candle.OpenTime,
                candle.Open,
                candle.High,
                candle.Low,
                candle.Close,
                candle.Volume,
                candle.CloseTime
            ));
        }
    }

    /// <summary>
    /// Parse DateTime from string - supports both Unix timestamp (ms) and ISO format
    /// </summary>
    private DateTime ParseDateTime(string value)
    {
        // Try Unix timestamp (milliseconds)
        if (long.TryParse(value, out long timestamp))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;
        }

        // Try ISO format
        return DateTime.Parse(value);
    }

    /// <summary>
    /// Load candles from CSV file
    /// </summary>
    public async Task<List<Candle>> LoadFromCsvAsync(string filePath)
    {
        var candles = new List<Candle>();
        using var reader = new StreamReader(filePath);

        // Skip header
        await reader.ReadLineAsync();
        
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(line)) continue;

            var parts = line.Split(',');
            if (parts.Length < 7) continue;

            // Parse dates - support both Unix timestamp (ms) and ISO format
            DateTime openTime = ParseDateTime(parts[0]);
            DateTime closeTime = ParseDateTime(parts[6]);

            candles.Add(new Candle(
                openTime,
                decimal.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                decimal.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                decimal.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture),
                decimal.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture),
                decimal.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture),
                closeTime
            ));
        }

        return candles;
    }

    private static List<Candle> FilterCandlesByRange(List<Candle> candles, DateTime startTime, DateTime endTime)
    {
        return candles
            .Where(c => c.OpenTime >= startTime && c.CloseTime <= endTime)
            .OrderBy(c => c.OpenTime)
            .ToList();
    }

    private sealed record CachedCsvInfo(
        string Path,
        string Symbol,
        KlineInterval Interval,
        DateTime Start,
        DateTime End);

    private static CachedCsvInfo? FindCachedFile(
        string directory,
        string symbol,
        KlineInterval interval,
        DateTime startTime,
        DateTime endTime)
    {
        if (!Directory.Exists(directory))
            return null;

        CachedCsvInfo? bestMatch = null;
        foreach (var file in Directory.GetFiles(directory, "*.csv"))
        {
            if (!TryParseCachedFile(file, out var info))
                continue;

            if (!info.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                continue;

            if (info.Interval != interval)
                continue;

            if (info.Start > startTime || info.End < endTime)
                continue;

            if (bestMatch == null || (info.End - info.Start) < (bestMatch.End - bestMatch.Start))
            {
                bestMatch = info;
            }
        }

        return bestMatch;
    }

    private static bool TryParseCachedFile(string filePath, out CachedCsvInfo info)
    {
        info = null!;
        var name = Path.GetFileNameWithoutExtension(filePath);
        var parts = name.Split('_');
        if (parts.Length < 4)
            return false;

        var symbol = parts[0];
        var intervalRaw = parts[1];
        var startRaw = parts[2];
        var endRaw = parts[3];

        if (!DateTime.TryParse(startRaw, out var start))
            return false;
        if (!DateTime.TryParse(endRaw, out var end))
            return false;

        var interval = KlineIntervalExtensions.Parse(intervalRaw);
        info = new CachedCsvInfo(filePath, symbol, interval, start, end);
        return true;
    }

    private static string BuildDataFilePath(
        string directory,
        string symbol,
        KlineInterval interval,
        DateTime startTime,
        DateTime endTime)
    {
        var intervalToken = interval.ToShortString();
        var startToken = startTime.ToString("yyyy-MM-dd");
        var endToken = endTime.ToString("yyyy-MM-dd");
        var fileName = $"{symbol}_{intervalToken}_{startToken}_{endToken}.csv";
        return Path.Combine(directory, fileName);
    }

    /// <summary>
    /// Get available trading pairs
    /// </summary>
    public async Task<List<string>> GetAvailableSymbolsAsync(string quoteAsset = "USDT")
    {
        var result = await _client.SpotApi.ExchangeData.GetExchangeInfoAsync();
        
        if (!result.Success)
            throw new Exception($"Failed to get symbols: {result.Error?.Message}");

        return result.Data.Symbols
            .Where(s => s.QuoteAsset == quoteAsset && s.Status == SymbolStatus.Trading)
            .Select(s => s.Name)
            .OrderBy(s => s)
            .ToList();
    }
}

public static class KlineIntervalExtensions
{
    public static KlineInterval Parse(string interval) => interval.ToLower() switch
    {
        "1m" or "oneminute" => KlineInterval.OneMinute,
        "3m" or "threeminutes" => KlineInterval.ThreeMinutes,
        "5m" or "fiveminutes" => KlineInterval.FiveMinutes,
        "15m" or "fifteenminutes" => KlineInterval.FifteenMinutes,
        "30m" or "thirtyminutes" => KlineInterval.ThirtyMinutes,
        "1h" or "onehour" => KlineInterval.OneHour,
        "2h" or "twohour" => KlineInterval.TwoHour,
        "4h" or "fourhour" => KlineInterval.FourHour,
        "6h" or "sixhour" => KlineInterval.SixHour,
        "8h" or "eighthour" => KlineInterval.EightHour,
        "12h" or "twelvehour" => KlineInterval.TwelveHour,
        "1d" or "oneday" => KlineInterval.OneDay,
        "3d" or "threeday" => KlineInterval.ThreeDay,
        "1w" or "oneweek" => KlineInterval.OneWeek,
        "1mo" or "onemonth" => KlineInterval.OneMonth,
        _ => KlineInterval.OneDay
    };

    public static string ToShortString(this KlineInterval interval) => interval switch
    {
        KlineInterval.OneMinute => "1m",
        KlineInterval.ThreeMinutes => "3m",
        KlineInterval.FiveMinutes => "5m",
        KlineInterval.FifteenMinutes => "15m",
        KlineInterval.ThirtyMinutes => "30m",
        KlineInterval.OneHour => "1h",
        KlineInterval.TwoHour => "2h",
        KlineInterval.FourHour => "4h",
        KlineInterval.SixHour => "6h",
        KlineInterval.EightHour => "8h",
        KlineInterval.TwelveHour => "12h",
        KlineInterval.OneDay => "1d",
        KlineInterval.ThreeDay => "3d",
        KlineInterval.OneWeek => "1w",
        KlineInterval.OneMonth => "1mo",
        _ => "1d"
    };
}
