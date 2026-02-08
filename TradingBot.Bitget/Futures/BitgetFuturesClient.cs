using Bitget.Net.Clients;
using Bitget.Net.Enums;
using Bitget.Net.Enums.V2;
using TradingBot.Core.Models;
using TradingBot.Bitget.Futures.Interfaces;
using TradingBot.Bitget.Common;
using Serilog;
using CorePositionSide = TradingBot.Core.Models.PositionSide;
using CoreKlineInterval = TradingBot.Core.Models.KlineInterval;
using CoreMarginType = TradingBot.Core.Models.MarginType;
using BitgetPositionSide = Bitget.Net.Enums.V2.PositionSide;

namespace TradingBot.Bitget.Futures;

/// <summary>
/// Bitget Futures (USDT-M) API client implementation using JK.Bitget.Net v3.4.0
/// </summary>
public class BitgetFuturesClient : IBitgetFuturesClient
{
    private readonly BitgetRestClient _client;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _exchangeInfoLock = new(1, 1);
    private HashSet<string>? _symbolsCache;
    private DateTime _symbolsCacheUpdatedAt = DateTime.MinValue;
    private static readonly TimeSpan SymbolsCacheTtl = TimeSpan.FromMinutes(30);

    public BitgetFuturesClient(BitgetRestClient client, ILogger? logger = null)
    {
        _client = client;
        _logger = logger ?? Log.ForContext<BitgetFuturesClient>();
    }

    #region IBitgetClient Implementation

    public async Task<List<Candle>> GetHistoricalKlinesAsync(
        string symbol,
        CoreKlineInterval interval,
        DateTime startTime,
        DateTime? endTime = null,
        int limit = 1000,
        CancellationToken ct = default)
    {
        var intervalBitget = BitgetHelpers.MapKlineInterval(interval);

        var result = await _client.FuturesApiV2.ExchangeData.GetKlinesAsync(
            BitgetProductTypeV2.UsdtFutures,
            symbol,
            intervalBitget,
            klineType: null,  // Can be null for regular klines
            startTime: startTime,
            endTime: endTime,
            limit: limit,
            ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to get Futures historical klines for {Symbol}: {Error}", symbol, result.Error?.Message);
            throw new Exception($"Failed to get historical klines: {result.Error?.Message}");
        }

        return result.Data.Select(k => new Candle(
            OpenTime: k.OpenTime,
            Open: k.OpenPrice,
            High: k.HighPrice,
            Low: k.LowPrice,
            Close: k.ClosePrice,
            Volume: k.Volume,
            CloseTime: k.OpenTime.AddSeconds(GetIntervalSeconds(interval))
        )).ToList();
    }

    public async Task<decimal> GetBalanceAsync(string asset, CancellationToken ct = default)
    {
        // For Bitget, we need to query balances and find the specific asset
        var balancesResult = await _client.FuturesApiV2.Account.GetBalancesAsync(BitgetProductTypeV2.UsdtFutures, ct);

        if (!balancesResult.Success)
        {
            _logger.Error("Failed to get Futures account balances: {Error}", balancesResult.Error?.Message);
            throw new Exception($"Failed to get account balances: {balancesResult.Error?.Message}");
        }

        var assetBalance = balancesResult.Data?.FirstOrDefault(b => b.MarginAsset.Equals(asset, StringComparison.OrdinalIgnoreCase));
        return assetBalance?.Available ?? 0m;
    }

    public async Task<bool> TestConnectivityAsync(CancellationToken ct = default)
    {
        try
        {
            // Check if API clients are initialized
            if (_client?.FuturesApiV2?.Account == null)
            {
                _logger.Error("BitgetRestClient.FuturesApiV2.Account is not initialized");
                return false;
            }

            // Test with authenticated endpoint (balances)
            // Note: GetServerTimeAsync has issues with DemoTrading environment in Bitget.Net SDK
            var accountResult = await _client.FuturesApiV2.Account.GetBalancesAsync(BitgetProductTypeV2.UsdtFutures, ct);
            if (!accountResult.Success)
            {
                _logger.Warning("Bitget Futures account request failed: {Error}", accountResult.Error?.Message);

                // In demo mode, environment mismatch can happen when socket/rest endpoints are mixed.
                // Allow startup to continue while emitting diagnostics.
                if (accountResult.Error?.Message?.Contains("environment") == true)
                {
                    _logger.Warning("⚠️ Bitget environment mismatch detected - proceeding with caution");
                    _logger.Information("Note: Verify demo environment name is 'demo' and websocket endpoint is wss://wspap.bitget.com");
                    return true; // Allow startup despite environment mismatch
                }

                return false;
            }

            _logger.Information("Bitget Futures API connectivity test passed");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Bitget Futures API connectivity test failed");
            return false;
        }
    }

    #endregion

    #region Futures-Specific Methods

    public async Task<FuturesPosition?> GetPositionAsync(string symbol, CancellationToken ct = default)
    {
        var result = await _client.FuturesApiV2.Trading.GetPositionAsync(BitgetProductTypeV2.UsdtFutures, symbol, "USDT", ct);

        if (!result.Success)
        {
            _logger.Error("Failed to get position for {Symbol}: {Error}", symbol, result.Error?.Message);
            return null;
        }

        // GetPositionAsync returns an array - get the first position with non-zero quantity
        var positions = result.Data;
        var position = positions?.FirstOrDefault(p => Math.Abs(p.Total) > 0);
        if (position == null)
            return null;

        return new FuturesPosition
        {
            Symbol = position.Symbol,
            Side = (position.PositionSide == BitgetPositionSide.Long) ? CorePositionSide.Long : CorePositionSide.Short,
            Quantity = Math.Abs(position.Total),
            EntryPrice = position.AverageOpenPrice,
            MarkPrice = position.MarkPrice,
            UnrealizedPnl = position.UnrealizedProfitAndLoss,
            LiquidationPrice = position.LiquidationPrice,
            Leverage = (int)position.Leverage,
            MarginType = BitgetHelpers.MapMarginMode(position.MarginMode.ToString()),
            InitialMargin = 0m,
            MaintMargin = 0m
        };
    }

    public async Task<List<FuturesPosition>> GetAllPositionsAsync(CancellationToken ct = default)
    {
        var result = await _client.FuturesApiV2.Trading.GetPositionsAsync(BitgetProductTypeV2.UsdtFutures, "USDT", ct);

        if (!result.Success)
        {
            _logger.Error("Failed to get all positions: {Error}", result.Error?.Message);
            return new List<FuturesPosition>();
        }

        return result.Data
            .Where(p => Math.Abs(p.Total) > 0)
            .Select(p => new FuturesPosition
            {
                Symbol = p.Symbol,
                Side = (p.PositionSide == BitgetPositionSide.Long) ? CorePositionSide.Long : CorePositionSide.Short,
                Quantity = Math.Abs(p.Total),
                EntryPrice = p.AverageOpenPrice,
                MarkPrice = p.MarkPrice,
                UnrealizedPnl = p.UnrealizedProfitAndLoss,
                LiquidationPrice = p.LiquidationPrice,
                Leverage = (int)p.Leverage,
                MarginType = BitgetHelpers.MapMarginMode(p.MarginMode.ToString()),
                InitialMargin = 0m,
                MaintMargin = 0m
            })
            .ToList();
    }

    public async Task<bool> SetLeverageAsync(string symbol, int leverage, CancellationToken ct = default)
    {
        _logger.Information("Setting leverage for {Symbol} to {Leverage}x", symbol, leverage);

        var result = await _client.FuturesApiV2.Account.SetLeverageAsync(
            BitgetProductTypeV2.UsdtFutures,
            symbol,
            "USDT",
            leverage,
            ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to set leverage for {Symbol}: {Error}", symbol, result.Error?.Message);
            return false;
        }

        _logger.Information("Leverage set to {Leverage}x for {Symbol}", leverage, symbol);
        return true;
    }

    public async Task<bool> SetMarginTypeAsync(string symbol, CoreMarginType marginType, CancellationToken ct = default)
    {
        _logger.Information("Setting margin type for {Symbol} to {MarginType}", symbol, marginType);

        var bitgetMarginMode = BitgetHelpers.MapMarginType(marginType);

        var result = await _client.FuturesApiV2.Account.SetMarginModeAsync(
            BitgetProductTypeV2.UsdtFutures,
            symbol,
            "USDT",
            mode: bitgetMarginMode,
            ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to set margin type for {Symbol}: {Error}", symbol, result.Error?.Message);
            return false;
        }

        _logger.Information("Margin type set to {MarginType} for {Symbol}", marginType, symbol);
        return true;
    }

    public async Task<LeverageInfo> GetLeverageInfoAsync(string symbol, CancellationToken ct = default)
    {
        var posResult = await _client.FuturesApiV2.Trading.GetPositionAsync(BitgetProductTypeV2.UsdtFutures, symbol, "USDT", ct);

        if (!posResult.Success)
        {
            _logger.Error("Failed to get leverage info for {Symbol}: {Error}", symbol, posResult.Error?.Message);
            throw new Exception($"Failed to get leverage info: {posResult.Error?.Message}");
        }

        var positions = posResult.Data;
        var position = positions?.FirstOrDefault();
        int currentLeverage = position != null ? (int)position.Leverage : 1;

        int maxLeverage = 125;
        decimal maxNotional = 0;

        return new LeverageInfo
        {
            Symbol = symbol,
            CurrentLeverage = currentLeverage,
            MaxLeverage = maxLeverage,
            MaxNotionalValue = maxNotional
        };
    }

    public async Task<decimal> GetLiquidationPriceAsync(string symbol, CancellationToken ct = default)
    {
        var position = await GetPositionAsync(symbol, ct);
        return position?.LiquidationPrice ?? 0m;
    }

    public async Task<decimal> GetMarkPriceAsync(string symbol, CancellationToken ct = default)
    {
        var result = await _client.FuturesApiV2.ExchangeData.GetTickerAsync(BitgetProductTypeV2.UsdtFutures, symbol, ct);

        if (!result.Success)
        {
            _logger.Error("Failed to get mark price for {Symbol}: {Error}", symbol, result.Error?.Message);
            throw new Exception($"Failed to get mark price: {result.Error?.Message}");
        }

        return result.Data?.MarkPrice ?? 0m;
    }

    public async Task<bool> SymbolExistsAsync(string symbol, CancellationToken ct = default)
    {
        var symbols = await GetSymbolsAsync(ct);
        return symbols.Contains(symbol);
    }

    public async Task<HashSet<string>> GetAllSymbolsAsync(CancellationToken ct = default)
    {
        return await GetSymbolsAsync(ct);
    }

    #endregion

    private async Task<HashSet<string>> GetSymbolsAsync(CancellationToken ct)
    {
        if (_symbolsCache != null && DateTime.UtcNow - _symbolsCacheUpdatedAt < SymbolsCacheTtl)
        {
            return _symbolsCache;
        }

        await _exchangeInfoLock.WaitAsync(ct);
        try
        {
            if (_symbolsCache != null && DateTime.UtcNow - _symbolsCacheUpdatedAt < SymbolsCacheTtl)
            {
                return _symbolsCache;
            }

            // For Bitget, we need to fetch all contracts (symbols)
            // Since there's no GetSymbolsAsync, we'll get tickers which includes all active symbols
            var tickersResult = await _client.FuturesApiV2.ExchangeData.GetTickersAsync(BitgetProductTypeV2.UsdtFutures, ct);
            if (!tickersResult.Success)
            {
                _logger.Error("Failed to get Futures tickers: {Error}", tickersResult.Error?.Message);
                throw new Exception($"Failed to get tickers: {tickersResult.Error?.Message}");
            }

            _symbolsCache = tickersResult.Data
                .Select(t => t.Symbol)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _symbolsCacheUpdatedAt = DateTime.UtcNow;

            return _symbolsCache;
        }
        finally
        {
            _exchangeInfoLock.Release();
        }
    }

    private static int GetIntervalSeconds(CoreKlineInterval interval)
    {
        return interval switch
        {
            CoreKlineInterval.OneMinute => 60,
            CoreKlineInterval.FiveMinutes => 300,
            CoreKlineInterval.FifteenMinutes => 900,
            CoreKlineInterval.ThirtyMinutes => 1800,
            CoreKlineInterval.OneHour => 3600,
            CoreKlineInterval.FourHour => 14400,
            CoreKlineInterval.OneDay => 86400,
            CoreKlineInterval.OneWeek => 604800,
            _ => 60
        };
    }
}
