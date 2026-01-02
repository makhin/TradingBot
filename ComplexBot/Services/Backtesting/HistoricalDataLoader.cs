using Binance.Net.Clients;
using Binance.Net.Enums;
using CryptoExchange.Net.Authentication;
using ComplexBot.Models;

namespace ComplexBot.Services.Backtesting;

public class HistoricalDataLoader
{
    private readonly BinanceRestClient _client;
    
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
    /// Save candles to CSV file
    /// </summary>
    public async Task SaveToCsvAsync(List<Candle> candles, string filePath)
    {
        using var writer = new StreamWriter(filePath);
        await writer.WriteLineAsync("OpenTime,Open,High,Low,Close,Volume,CloseTime");
        
        foreach (var candle in candles)
        {
            await writer.WriteLineAsync(
                $"{candle.OpenTime:yyyy-MM-dd HH:mm:ss}," +
                $"{candle.Open},{candle.High},{candle.Low},{candle.Close}," +
                $"{candle.Volume},{candle.CloseTime:yyyy-MM-dd HH:mm:ss}"
            );
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
                decimal.Parse(parts[1]),
                decimal.Parse(parts[2]),
                decimal.Parse(parts[3]),
                decimal.Parse(parts[4]),
                decimal.Parse(parts[5]),
                closeTime
            ));
        }

        return candles;
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
        "1m" => KlineInterval.OneMinute,
        "5m" => KlineInterval.FiveMinutes,
        "15m" => KlineInterval.FifteenMinutes,
        "30m" => KlineInterval.ThirtyMinutes,
        "1h" => KlineInterval.OneHour,
        "4h" => KlineInterval.FourHour,
        "1d" => KlineInterval.OneDay,
        "1w" => KlineInterval.OneWeek,
        _ => KlineInterval.OneDay
    };
}
