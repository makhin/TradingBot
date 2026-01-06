using Binance.Net.Clients;
using ComplexBot.Models;
using CryptoExchange.Net.Authentication;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using System.Globalization;

namespace ComplexBot.Services.Backtesting;

public class HistoricalDataLoader
{
    private readonly BinanceRestClient _client;
    private const string CsvDateTimeFormat = "yyyy-MM-dd HH:mm:ss";

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
                interval.ToBinanceInterval(),
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
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));
        csv.Context.RegisterClassMap<CandleCsvMap>();

        await csv.WriteRecordsAsync(candles.Select(candle => new CandleCsvRecord
        {
            OpenTime = candle.OpenTime,
            Open = candle.Open,
            High = candle.High,
            Low = candle.Low,
            Close = candle.Close,
            Volume = candle.Volume,
            CloseTime = candle.CloseTime
        }));
    }

    /// <summary>
    /// Load candles from CSV file
    /// </summary>
    public async Task<List<Candle>> LoadFromCsvAsync(string filePath)
    {
        var candles = new List<Candle>();
        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture));
        csv.Context.RegisterClassMap<CandleCsvMap>();

        await foreach (var record in csv.GetRecordsAsync<CandleCsvRecord>())
        {
            candles.Add(new Candle(
                record.OpenTime,
                record.Open,
                record.High,
                record.Low,
                record.Close,
                record.Volume,
                record.CloseTime
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
            .Where(s => s.QuoteAsset == quoteAsset && s.Status == Binance.Net.Enums.SymbolStatus.Trading)
            .Select(s => s.Name)
            .OrderBy(s => s)
            .ToList();
    }

    private sealed class CandleCsvRecord
    {
        public DateTime OpenTime { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public DateTime CloseTime { get; set; }
    }

    private sealed class CandleCsvMap : ClassMap<CandleCsvRecord>
    {
        public CandleCsvMap()
        {
            Map(m => m.OpenTime).Name("OpenTime").Index(0).TypeConverter<CandleDateTimeConverter>();
            Map(m => m.Open).Name("Open").Index(1);
            Map(m => m.High).Name("High").Index(2);
            Map(m => m.Low).Name("Low").Index(3);
            Map(m => m.Close).Name("Close").Index(4);
            Map(m => m.Volume).Name("Volume").Index(5);
            Map(m => m.CloseTime).Name("CloseTime").Index(6).TypeConverter<CandleDateTimeConverter>();
        }
    }

    private sealed class CandleDateTimeConverter : DefaultTypeConverter
    {
        private static readonly string[] DateFormats =
        [
            CsvDateTimeFormat,
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.FFFFFFFK",
            "O"
        ];

        public override object? ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return base.ConvertFromString(text, row, memberMapData);
            }

            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;
            }

            if (DateTime.TryParseExact(
                    text,
                    DateFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return parsed;
            }

            if (DateTime.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out parsed))
            {
                return parsed;
            }

            throw new TypeConverterException(this, memberMapData, text, row.Context, $"Invalid date value '{text}'.");
        }

        public override string? ConvertToString(object? value, IWriterRow row, MemberMapData memberMapData)
        {
            return value is DateTime dateTime
                ? dateTime.ToString(CsvDateTimeFormat, CultureInfo.InvariantCulture)
                : base.ConvertToString(value, row, memberMapData);
        }
    }
}
