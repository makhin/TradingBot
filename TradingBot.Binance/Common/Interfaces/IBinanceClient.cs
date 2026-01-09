using TradingBot.Core.Models;

namespace TradingBot.Binance.Common.Interfaces;

/// <summary>
/// Common interface for Binance API clients (Spot/Futures)
/// </summary>
public interface IBinanceClient
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
}
