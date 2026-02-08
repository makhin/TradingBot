using TradingBot.Core.Models;

namespace TradingBot.Bitget.Futures.Interfaces;

/// <summary>
/// Interface for Bitget Futures API operations
/// </summary>
public interface IBitgetFuturesClient
{
    /// <summary>
    /// Gets historical klines/candles for a symbol
    /// </summary>
    Task<List<Candle>> GetHistoricalKlinesAsync(
        string symbol,
        KlineInterval interval,
        DateTime startTime,
        DateTime? endTime = null,
        int limit = 1000,
        CancellationToken ct = default);

    /// <summary>
    /// Gets current account balance
    /// </summary>
    Task<decimal> GetBalanceAsync(string asset, CancellationToken ct = default);

    /// <summary>
    /// Checks if API credentials are valid
    /// </summary>
    Task<bool> TestConnectivityAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets current position for a symbol
    /// </summary>
    Task<FuturesPosition?> GetPositionAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// Gets all open positions
    /// </summary>
    Task<List<FuturesPosition>> GetAllPositionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Sets leverage for a symbol
    /// </summary>
    Task<bool> SetLeverageAsync(string symbol, int leverage, CancellationToken ct = default);

    /// <summary>
    /// Sets margin type (Isolated/Cross) for a symbol
    /// </summary>
    Task<bool> SetMarginTypeAsync(string symbol, MarginType marginType, CancellationToken ct = default);

    /// <summary>
    /// Gets leverage information for a symbol
    /// </summary>
    Task<LeverageInfo> GetLeverageInfoAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// Gets liquidation price for a symbol position
    /// </summary>
    Task<decimal> GetLiquidationPriceAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// Gets mark price for a symbol
    /// </summary>
    Task<decimal> GetMarkPriceAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// Checks whether a futures symbol exists on the exchange
    /// </summary>
    Task<bool> SymbolExistsAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// Gets all available futures trading symbols
    /// </summary>
    Task<HashSet<string>> GetAllSymbolsAsync(CancellationToken ct = default);
}
