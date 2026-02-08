using TradingBot.Core.Exchanges;
using TradingBot.Core.Models;
using TradingBot.Bitget.Futures.Interfaces;

namespace TradingBot.Bitget.Futures.Adapters;

/// <summary>
/// Adapter that wraps BitgetFuturesClient and exposes generic IFuturesExchangeClient interface
/// Enables exchange-agnostic business logic while preserving existing Bitget implementation
/// </summary>
public class BitgetFuturesClientAdapter : IFuturesExchangeClient
{
    private readonly IBitgetFuturesClient _bitgetClient;

    public string ExchangeName => "Bitget";

    public BitgetFuturesClientAdapter(IBitgetFuturesClient bitgetClient)
    {
        _bitgetClient = bitgetClient;
    }

    // Delegate all calls to the underlying Bitget client
    public Task<List<Candle>> GetHistoricalKlinesAsync(
        string symbol,
        KlineInterval interval,
        DateTime startTime,
        DateTime? endTime = null,
        int limit = 1000,
        CancellationToken ct = default)
        => _bitgetClient.GetHistoricalKlinesAsync(symbol, interval, startTime, endTime, limit, ct);

    public Task<decimal> GetBalanceAsync(string asset, CancellationToken ct = default)
        => _bitgetClient.GetBalanceAsync(asset, ct);

    public Task<bool> TestConnectivityAsync(CancellationToken ct = default)
        => _bitgetClient.TestConnectivityAsync(ct);

    public Task<FuturesPosition?> GetPositionAsync(string symbol, CancellationToken ct = default)
        => _bitgetClient.GetPositionAsync(symbol, ct);

    public Task<List<FuturesPosition>> GetAllPositionsAsync(CancellationToken ct = default)
        => _bitgetClient.GetAllPositionsAsync(ct);

    public Task<bool> SetLeverageAsync(string symbol, int leverage, CancellationToken ct = default)
        => _bitgetClient.SetLeverageAsync(symbol, leverage, ct);

    public Task<bool> SetMarginTypeAsync(string symbol, MarginType marginType, CancellationToken ct = default)
        => _bitgetClient.SetMarginTypeAsync(symbol, marginType, ct);

    public Task<LeverageInfo> GetLeverageInfoAsync(string symbol, CancellationToken ct = default)
        => _bitgetClient.GetLeverageInfoAsync(symbol, ct);

    public Task<decimal> GetLiquidationPriceAsync(string symbol, CancellationToken ct = default)
        => _bitgetClient.GetLiquidationPriceAsync(symbol, ct);

    public Task<decimal> GetMarkPriceAsync(string symbol, CancellationToken ct = default)
        => _bitgetClient.GetMarkPriceAsync(symbol, ct);

    public Task<bool> SymbolExistsAsync(string symbol, CancellationToken ct = default)
        => _bitgetClient.SymbolExistsAsync(symbol, ct);

    public Task<HashSet<string>> GetAllSymbolsAsync(CancellationToken ct = default)
        => _bitgetClient.GetAllSymbolsAsync(ct);
}
