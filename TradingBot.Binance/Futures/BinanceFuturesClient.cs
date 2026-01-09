using Binance.Net.Clients;
using Binance.Net.Enums;
using TradingBot.Core.Models;
using TradingBot.Binance.Futures.Interfaces;
using TradingBot.Binance.Futures.Models;
using Serilog;
using BinanceKlineInterval = Binance.Net.Enums.KlineInterval;
using BinancePositionSide = Binance.Net.Enums.PositionSide;

namespace TradingBot.Binance.Futures;

/// <summary>
/// Binance Futures (USDT-M) API client implementation
/// </summary>
public class BinanceFuturesClient : IBinanceFuturesClient
{
    private readonly BinanceRestClient _client;
    private readonly ILogger _logger;

    public BinanceFuturesClient(BinanceRestClient client, ILogger? logger = null)
    {
        _client = client;
        _logger = logger ?? Log.ForContext<BinanceFuturesClient>();
    }

    #region IBinanceClient Implementation

    public async Task<List<Candle>> GetHistoricalKlinesAsync(
        string symbol,
        TradingBot.Core.Models.KlineInterval interval,
        DateTime startTime,
        DateTime? endTime = null,
        int limit = 1000,
        CancellationToken ct = default)
    {
        var binanceInterval = MapKlineInterval(interval);
        var result = await _client.UsdFuturesApi.ExchangeData.GetKlinesAsync(
            symbol,
            binanceInterval,
            startTime,
            endTime,
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
            CloseTime: k.CloseTime
        )).ToList();
    }

    public async Task<decimal> GetBalanceAsync(string asset, CancellationToken ct = default)
    {
        var accountInfo = await _client.UsdFuturesApi.Account.GetBalancesAsync(ct: ct);

        if (!accountInfo.Success)
        {
            _logger.Error("Failed to get Futures account balances: {Error}", accountInfo.Error?.Message);
            throw new Exception($"Failed to get account balances: {accountInfo.Error?.Message}");
        }

        var balance = accountInfo.Data.FirstOrDefault(b => b.Asset == asset);
        return balance?.AvailableBalance ?? 0m;
    }

    public async Task<bool> TestConnectivityAsync(CancellationToken ct = default)
    {
        try
        {
            var pingResult = await _client.UsdFuturesApi.ExchangeData.PingAsync(ct);
            if (!pingResult.Success)
            {
                _logger.Warning("Futures ping failed: {Error}", pingResult.Error?.Message);
                return false;
            }

            var accountResult = await _client.UsdFuturesApi.Account.GetBalancesAsync(ct: ct);
            if (!accountResult.Success)
            {
                _logger.Warning("Futures account balances failed: {Error}", accountResult.Error?.Message);
                return false;
            }

            _logger.Information("Futures API connectivity test passed");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Futures API connectivity test failed");
            return false;
        }
    }

    #endregion

    #region Futures-Specific Methods

    public async Task<FuturesPosition?> GetPositionAsync(string symbol, CancellationToken ct = default)
    {
        var result = await _client.UsdFuturesApi.Account.GetPositionInformationAsync(symbol, ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to get position for {Symbol}: {Error}", symbol, result.Error?.Message);
            return null;
        }

        var position = result.Data.FirstOrDefault(p => p.Symbol == symbol && p.Quantity != 0);
        if (position == null)
            return null;

        return new FuturesPosition
        {
            Symbol = position.Symbol,
            Side = position.Quantity > 0 ? Models.PositionSide.Long : Models.PositionSide.Short,
            Quantity = Math.Abs(position.Quantity),
            EntryPrice = position.EntryPrice,
            MarkPrice = position.MarkPrice,
            UnrealizedPnl = position.UnrealizedPnl,
            LiquidationPrice = position.LiquidationPrice,
            Leverage = position.Leverage,
            MarginType = Models.MarginType.Cross, // Default to Cross, as isolated info not in response
            InitialMargin = 0m, // Not available in GetPositionInformationAsync response
            MaintMargin = 0m // Not available in GetPositionInformationAsync response
        };
    }

    public async Task<List<FuturesPosition>> GetAllPositionsAsync(CancellationToken ct = default)
    {
        var result = await _client.UsdFuturesApi.Account.GetPositionInformationAsync(ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to get all positions: {Error}", result.Error?.Message);
            return new List<FuturesPosition>();
        }

        return result.Data
            .Where(p => p.Quantity != 0)
            .Select(p => new FuturesPosition
            {
                Symbol = p.Symbol,
                Side = p.Quantity > 0 ? Models.PositionSide.Long : Models.PositionSide.Short,
                Quantity = Math.Abs(p.Quantity),
                EntryPrice = p.EntryPrice,
                MarkPrice = p.MarkPrice,
                UnrealizedPnl = p.UnrealizedPnl,
                LiquidationPrice = p.LiquidationPrice,
                Leverage = p.Leverage,
                MarginType = Models.MarginType.Cross, // Default to Cross, as isolated info not in response
                InitialMargin = 0m, // Not available in GetPositionInformationAsync response
                MaintMargin = 0m // Not available in GetPositionInformationAsync response
            })
            .ToList();
    }

    public async Task<bool> SetLeverageAsync(string symbol, int leverage, CancellationToken ct = default)
    {
        _logger.Information("Setting leverage for {Symbol} to {Leverage}x", symbol, leverage);

        var result = await _client.UsdFuturesApi.Account.ChangeInitialLeverageAsync(
            symbol,
            leverage,
            ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to set leverage for {Symbol}: {Error}", symbol, result.Error?.Message);
            return false;
        }

        _logger.Information("Leverage set to {Leverage}x for {Symbol}", result.Data.Leverage, symbol);
        return true;
    }

    public async Task<bool> SetMarginTypeAsync(string symbol, MarginType marginType, CancellationToken ct = default)
    {
        _logger.Information("Setting margin type for {Symbol} to {MarginType}", symbol, marginType);

        var binanceMarginType = marginType == MarginType.Isolated
            ? FuturesMarginType.Isolated
            : FuturesMarginType.Cross;

        var result = await _client.UsdFuturesApi.Account.ChangeMarginTypeAsync(
            symbol,
            binanceMarginType,
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
        // Get current leverage from position info
        var posResult = await _client.UsdFuturesApi.Account.GetPositionInformationAsync(symbol, ct: ct);

        if (!posResult.Success)
        {
            _logger.Error("Failed to get leverage info for {Symbol}: {Error}", symbol, posResult.Error?.Message);
            throw new Exception($"Failed to get leverage info: {posResult.Error?.Message}");
        }

        var position = posResult.Data.FirstOrDefault(p => p.Symbol == symbol);
        int currentLeverage = position?.Leverage ?? 1;

        // Default max leverage (can be queried via GetLeverageBracketsAsync if needed)
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
        var result = await _client.UsdFuturesApi.ExchangeData.GetMarkPriceAsync(symbol, ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to get mark price for {Symbol}: {Error}", symbol, result.Error?.Message);
            throw new Exception($"Failed to get mark price: {result.Error?.Message}");
        }

        return result.Data.MarkPrice;
    }

    #endregion

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
