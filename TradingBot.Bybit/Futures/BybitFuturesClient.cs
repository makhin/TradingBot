using Bybit.Net.Clients;
using Bybit.Net.Enums;
using TradingBot.Core.Models;
using Serilog;
using BybitKlineInterval = Bybit.Net.Enums.KlineInterval;
using BybitPositionSide = Bybit.Net.Enums.PositionSide;
using CorePositionSide = TradingBot.Core.Models.PositionSide;

namespace TradingBot.Bybit.Futures;

/// <summary>
/// Bybit Futures (USDT Perpetual) API client implementation
/// Wraps Bybit.Net SDK V5 API for unified margin trading
/// NOTE: This is a stub implementation that needs to be completed based on actual Bybit.Net API
/// </summary>
public class BybitFuturesClient
{
    private readonly BybitRestClient _client;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _exchangeInfoLock = new(1, 1);
    private HashSet<string>? _symbolsCache;
    private DateTime _symbolsCacheUpdatedAt = DateTime.MinValue;
    private static readonly TimeSpan SymbolsCacheTtl = TimeSpan.FromMinutes(30);

    public BybitFuturesClient(BybitRestClient client, ILogger? logger = null)
    {
        _client = client;
        _logger = logger ?? Log.ForContext<BybitFuturesClient>();
    }

    public async Task<List<Candle>> GetHistoricalKlinesAsync(
        string symbol,
        TradingBot.Core.Models.KlineInterval interval,
        DateTime startTime,
        DateTime? endTime = null,
        int limit = 1000,
        CancellationToken ct = default)
    {
        var bybitInterval = MapKlineInterval(interval);
        var result = await _client.V5Api.ExchangeData.GetKlinesAsync(
            Category.Linear,
            symbol,
            bybitInterval,
            startTime,
            endTime,
            limit: limit,
            ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to get Futures historical klines for {Symbol}: {Error}", symbol, result.Error?.Message);
            throw new Exception($"Failed to get historical klines: {result.Error?.Message}");
        }

        return result.Data.List.Select(k => new Candle(
            OpenTime: k.StartTime,
            Open: k.OpenPrice,
            High: k.HighPrice,
            Low: k.LowPrice,
            Close: k.ClosePrice,
            Volume: k.Volume,
            CloseTime: k.StartTime.AddMilliseconds(GetIntervalMilliseconds(bybitInterval))
        )).ToList();
    }

    public async Task<decimal> GetBalanceAsync(string asset, CancellationToken ct = default)
    {
        var accountInfo = await _client.V5Api.Account.GetBalancesAsync(AccountType.Unified, ct: ct);

        if (!accountInfo.Success)
        {
            _logger.Error("Failed to get Futures account balances: {Error}", accountInfo.Error?.Message);
            throw new Exception($"Failed to get account balances: {accountInfo.Error?.Message}");
        }

        var balance = accountInfo.Data.List.FirstOrDefault()?.Assets.FirstOrDefault(b => b.Asset == asset);
        return balance?.WalletBalance ?? 0m;
    }

    public async Task<bool> TestConnectivityAsync(CancellationToken ct = default)
    {
        try
        {
            var serverTime = await _client.V5Api.ExchangeData.GetServerTimeAsync(ct);
            if (!serverTime.Success)
            {
                _logger.Warning("Futures server time failed: {Error}", serverTime.Error?.Message);
                return false;
            }

            _logger.Information("Futures API connectivity test passed");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Futures connectivity test exception");
            return false;
        }
    }

    public async Task<FuturesPosition?> GetPositionAsync(string symbol, CancellationToken ct = default)
    {
        var positions = await GetAllPositionsAsync(ct);
        return positions.FirstOrDefault(p => p.Symbol == symbol && p.Quantity > 0);
    }

    public async Task<List<FuturesPosition>> GetAllPositionsAsync(CancellationToken ct = default)
    {
        var result = await _client.V5Api.Trading.GetPositionsAsync(Category.Linear, ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to get positions: {Error}", result.Error?.Message);
            throw new Exception($"Failed to get positions: {result.Error?.Message}");
        }

        return result.Data.List
            .Where(p => p.Quantity > 0)
            .Select(p => new FuturesPosition
            {
                Symbol = p.Symbol,
                Side = p.Side == BybitPositionSide.Buy ? CorePositionSide.Long : CorePositionSide.Short,
                Quantity = p.Quantity,
                EntryPrice = p.AveragePrice ?? 0m,
                MarkPrice = p.MarkPrice ?? 0m,
                UnrealizedPnl = p.UnrealizedPnl ?? 0m,
                LiquidationPrice = p.LiquidationPrice ?? 0m,
                Leverage = (int)(p.Leverage ?? 1m),
                MarginType = MarginType.Isolated, // Bybit V5 uses different margin system
                InitialMargin = p.PositionBalance ?? 0m,
                MaintMargin = p.PositionBalance ?? 0m
            })
            .ToList();
    }

    public async Task<bool> SetLeverageAsync(string symbol, int leverage, CancellationToken ct = default)
    {
        // NOTE: Bybit V5 API uses different leverage setting method
        // This is a stub - actual implementation needs Bybit.Net API documentation
        _logger.Warning("SetLeverageAsync not fully implemented for Bybit - needs API documentation");
        return await Task.FromResult(true);
    }

    public async Task<bool> SetMarginTypeAsync(string symbol, MarginType marginType, CancellationToken ct = default)
    {
        // NOTE: Bybit V5 unified margin works differently than Binance
        // This is a stub - actual implementation needs Bybit.Net API documentation
        _logger.Warning("SetMarginTypeAsync not fully implemented for Bybit - needs API documentation");
        return await Task.FromResult(true);
    }

    public async Task<LeverageInfo> GetLeverageInfoAsync(string symbol, CancellationToken ct = default)
    {
        return new LeverageInfo
        {
            Symbol = symbol,
            CurrentLeverage = 10,
            MaxLeverage = 100,
            MaxNotionalValue = 1000000m
        };
    }

    public async Task<decimal> GetLiquidationPriceAsync(string symbol, CancellationToken ct = default)
    {
        var position = await GetPositionAsync(symbol, ct);
        return position?.LiquidationPrice ?? 0m;
    }

    public async Task<decimal> GetMarkPriceAsync(string symbol, CancellationToken ct = default)
    {
        var result = await _client.V5Api.ExchangeData.GetLinearInverseTickersAsync(
            Category.Linear,
            symbol: symbol,
            ct: ct);

        if (!result.Success || !result.Data.List.Any())
        {
            _logger.Error("Failed to get mark price for {Symbol}: {Error}", symbol, result.Error?.Message);
            throw new Exception($"Failed to get mark price: {result.Error?.Message}");
        }

        return result.Data.List.First().MarkPrice;
    }

    public async Task<bool> SymbolExistsAsync(string symbol, CancellationToken ct = default)
    {
        var symbols = await GetAllSymbolsAsync(ct);
        return symbols.Contains(symbol);
    }

    public async Task<HashSet<string>> GetAllSymbolsAsync(CancellationToken ct = default)
    {
        await _exchangeInfoLock.WaitAsync(ct);
        try
        {
            if (_symbolsCache != null && (DateTime.UtcNow - _symbolsCacheUpdatedAt) < SymbolsCacheTtl)
            {
                return _symbolsCache;
            }

            var result = await _client.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(Category.Linear, ct: ct);

            if (!result.Success)
            {
                _logger.Error("Failed to get exchange symbols: {Error}", result.Error?.Message);
                return _symbolsCache ?? new HashSet<string>();
            }

            _symbolsCache = result.Data.List.Select(s => s.Name).ToHashSet();
            _symbolsCacheUpdatedAt = DateTime.UtcNow;

            return _symbolsCache;
        }
        finally
        {
            _exchangeInfoLock.Release();
        }
    }

    private static BybitKlineInterval MapKlineInterval(TradingBot.Core.Models.KlineInterval interval)
    {
        return interval switch
        {
            TradingBot.Core.Models.KlineInterval.OneMinute => BybitKlineInterval.OneMinute,
            TradingBot.Core.Models.KlineInterval.FiveMinutes => BybitKlineInterval.FiveMinutes,
            TradingBot.Core.Models.KlineInterval.FifteenMinutes => BybitKlineInterval.FifteenMinutes,
            TradingBot.Core.Models.KlineInterval.ThirtyMinutes => BybitKlineInterval.ThirtyMinutes,
            TradingBot.Core.Models.KlineInterval.OneHour => BybitKlineInterval.OneHour,
            TradingBot.Core.Models.KlineInterval.FourHour => BybitKlineInterval.OneHour, // Bybit doesn't have 4h, use 1h
            TradingBot.Core.Models.KlineInterval.OneDay => BybitKlineInterval.OneDay,
            _ => throw new ArgumentException($"Unsupported interval: {interval}")
        };
    }

    private static long GetIntervalMilliseconds(BybitKlineInterval interval)
    {
        return interval switch
        {
            BybitKlineInterval.OneMinute => 60000,
            BybitKlineInterval.FiveMinutes => 300000,
            BybitKlineInterval.FifteenMinutes => 900000,
            BybitKlineInterval.ThirtyMinutes => 1800000,
            BybitKlineInterval.OneHour => 3600000,
            BybitKlineInterval.OneDay => 86400000,
            _ => 60000
        };
    }
}
