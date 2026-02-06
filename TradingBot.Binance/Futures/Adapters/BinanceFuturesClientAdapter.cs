using TradingBot.Core.Exchanges;
using TradingBot.Core.Models;
using TradingBot.Binance.Futures.Interfaces;

namespace TradingBot.Binance.Futures.Adapters;

/// <summary>
/// Adapter that wraps BinanceFuturesClient and exposes generic IFuturesExchangeClient interface
/// Enables exchange-agnostic business logic while preserving existing Binance implementation
/// </summary>
public class BinanceFuturesClientAdapter : IFuturesExchangeClient
{
    private readonly IBinanceFuturesClient _binanceClient;

    public string ExchangeName => "Binance";

    public BinanceFuturesClientAdapter(IBinanceFuturesClient binanceClient)
    {
        _binanceClient = binanceClient;
    }

    // Delegate all calls to the underlying Binance client
    public Task<List<Candle>> GetHistoricalKlinesAsync(
        string symbol,
        KlineInterval interval,
        DateTime startTime,
        DateTime? endTime = null,
        int limit = 1000,
        CancellationToken ct = default)
        => _binanceClient.GetHistoricalKlinesAsync(symbol, interval, startTime, endTime, limit, ct);

    public Task<decimal> GetBalanceAsync(string asset, CancellationToken ct = default)
        => _binanceClient.GetBalanceAsync(asset, ct);

    public Task<bool> TestConnectivityAsync(CancellationToken ct = default)
        => _binanceClient.TestConnectivityAsync(ct);

    public Task<FuturesPosition?> GetPositionAsync(string symbol, CancellationToken ct = default)
        => _binanceClient.GetPositionAsync(symbol, ct);

    public Task<List<FuturesPosition>> GetAllPositionsAsync(CancellationToken ct = default)
        => _binanceClient.GetAllPositionsAsync(ct);

    public Task<bool> SetLeverageAsync(string symbol, int leverage, CancellationToken ct = default)
        => _binanceClient.SetLeverageAsync(symbol, leverage, ct);

    public Task<bool> SetMarginTypeAsync(string symbol, MarginType marginType, CancellationToken ct = default)
        => _binanceClient.SetMarginTypeAsync(symbol, marginType, ct);

    public Task<LeverageInfo> GetLeverageInfoAsync(string symbol, CancellationToken ct = default)
        => _binanceClient.GetLeverageInfoAsync(symbol, ct);

    public Task<decimal> GetLiquidationPriceAsync(string symbol, CancellationToken ct = default)
        => _binanceClient.GetLiquidationPriceAsync(symbol, ct);

    public Task<decimal> GetMarkPriceAsync(string symbol, CancellationToken ct = default)
        => _binanceClient.GetMarkPriceAsync(symbol, ct);

    public Task<bool> SymbolExistsAsync(string symbol, CancellationToken ct = default)
        => _binanceClient.SymbolExistsAsync(symbol, ct);

    public Task<HashSet<string>> GetAllSymbolsAsync(CancellationToken ct = default)
        => _binanceClient.GetAllSymbolsAsync(ct);
}
