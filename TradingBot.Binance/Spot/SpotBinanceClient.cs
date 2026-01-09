using Binance.Net.Clients;
using Binance.Net.Enums;
using TradingBot.Core.Models;
using TradingBot.Binance.Common.Interfaces;
using Serilog;
using BinanceKlineInterval = Binance.Net.Enums.KlineInterval;

namespace TradingBot.Binance.Spot;

/// <summary>
/// Binance Spot market API client implementation
/// </summary>
public class SpotBinanceClient : IBinanceClient
{
    private readonly BinanceRestClient _client;
    private readonly ILogger _logger;

    public SpotBinanceClient(BinanceRestClient client, ILogger? logger = null)
    {
        _client = client;
        _logger = logger ?? Log.ForContext<SpotBinanceClient>();
    }

    /// <summary>
    /// Gets historical klines/candles for a symbol
    /// </summary>
    public async Task<List<Candle>> GetHistoricalKlinesAsync(
        string symbol,
        TradingBot.Core.Models.KlineInterval interval,
        DateTime startTime,
        DateTime? endTime = null,
        int limit = 1000,
        CancellationToken ct = default)
    {
        var binanceInterval = MapKlineInterval(interval);
        var result = await _client.SpotApi.ExchangeData.GetKlinesAsync(
            symbol,
            binanceInterval,
            startTime,
            endTime,
            limit: limit,
            ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to get historical klines for {Symbol}: {Error}", symbol, result.Error?.Message);
            throw new Exception($"Failed to get historical klines: {result.Error?.Message}");
        }

        return result.Data.Select(k => new Candle(
            OpenTime: k.OpenTime,
            Open: k.OpenPrice,
            High: k.HighPrice,
            Low: k.LowPrice,
            Close: k.ClosePrice,
            Volume: k.Volume,
            CloseTime: k.CloseTime
        )).ToList();
    }

    /// <summary>
    /// Gets current account balance for an asset
    /// </summary>
    public async Task<decimal> GetBalanceAsync(string asset, CancellationToken ct = default)
    {
        var accountInfo = await _client.SpotApi.Account.GetAccountInfoAsync(ct: ct);

        if (!accountInfo.Success)
        {
            _logger.Error("Failed to get account info: {Error}", accountInfo.Error?.Message);
            throw new Exception($"Failed to get account info: {accountInfo.Error?.Message}");
        }

        var balance = accountInfo.Data.Balances.FirstOrDefault(b => b.Asset == asset);
        return balance?.Available ?? 0m;
    }

    /// <summary>
    /// Tests API connectivity and credentials
    /// </summary>
    public async Task<bool> TestConnectivityAsync(CancellationToken ct = default)
    {
        try
        {
            var pingResult = await _client.SpotApi.ExchangeData.PingAsync(ct);
            if (!pingResult.Success)
            {
                _logger.Warning("Ping failed: {Error}", pingResult.Error?.Message);
                return false;
            }

            var accountResult = await _client.SpotApi.Account.GetAccountInfoAsync(ct: ct);
            if (!accountResult.Success)
            {
                _logger.Warning("Account info failed: {Error}", accountResult.Error?.Message);
                return false;
            }

            _logger.Information("API connectivity test passed");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "API connectivity test failed");
            return false;
        }
    }

    /// <summary>
    /// Maps TradingBot KlineInterval to Binance KlineInterval
    /// </summary>
    private static BinanceKlineInterval MapKlineInterval(TradingBot.Core.Models.KlineInterval interval)
    {
        return interval switch
        {
            TradingBot.Core.Models.KlineInterval.OneMinute => BinanceKlineInterval.OneMinute,
            TradingBot.Core.Models.KlineInterval.FiveMinutes => BinanceKlineInterval.FiveMinutes,
            TradingBot.Core.Models.KlineInterval.FifteenMinutes => BinanceKlineInterval.FifteenMinutes,
            TradingBot.Core.Models.KlineInterval.ThirtyMinutes => BinanceKlineInterval.ThirtyMinutes,
            TradingBot.Core.Models.KlineInterval.OneHour => BinanceKlineInterval.OneHour,
            TradingBot.Core.Models.KlineInterval.FourHour => BinanceKlineInterval.FourHour,
            TradingBot.Core.Models.KlineInterval.OneDay => BinanceKlineInterval.OneDay,
            TradingBot.Core.Models.KlineInterval.OneWeek => BinanceKlineInterval.OneWeek,
            _ => throw new ArgumentException($"Unsupported interval: {interval}")
        };
    }
}
