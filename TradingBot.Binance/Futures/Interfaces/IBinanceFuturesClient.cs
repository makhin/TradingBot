using TradingBot.Binance.Common.Interfaces;
using TradingBot.Binance.Futures.Models;

namespace TradingBot.Binance.Futures.Interfaces;

/// <summary>
/// Interface for Binance Futures API operations
/// Extends base client with Futures-specific functionality
/// </summary>
public interface IBinanceFuturesClient : IBinanceClient
{
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
}
