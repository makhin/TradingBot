using TradingBot.Core.Models;

namespace TradingBot.Core.Exchanges;

/// <summary>
/// Base interface for exchange API clients
/// Provides common functionality across all exchanges (spot and futures)
/// </summary>
public interface IExchangeClient
{
    /// <summary>
    /// The name of the exchange (Binance, Bybit, etc.)
    /// </summary>
    string ExchangeName { get; }

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
