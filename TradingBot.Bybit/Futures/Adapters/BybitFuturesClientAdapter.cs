using TradingBot.Core.Exchanges;
using TradingBot.Core.Models;

namespace TradingBot.Bybit.Futures.Adapters;

/// <summary>
/// Adapter that wraps BybitFuturesClient and exposes generic IFuturesExchangeClient interface
/// Enables exchange-agnostic business logic while preserving Bybit implementation
/// </summary>
public class BybitFuturesClientAdapter : IFuturesExchangeClient
{
    private readonly BybitFuturesClient _bybitClient;

    public string ExchangeName => "Bybit";

    public BybitFuturesClientAdapter(BybitFuturesClient bybitClient)
    {
        _bybitClient = bybitClient;
    }

    public Task<List<Candle>> GetHistoricalKlinesAsync(
        string symbol,
        KlineInterval interval,
        DateTime startTime,
        DateTime? endTime = null,
        int limit = 1000,
        CancellationToken ct = default)
        => _bybitClient.GetHistoricalKlinesAsync(symbol, interval, startTime, endTime, limit, ct);

    public Task<decimal> GetBalanceAsync(string asset, CancellationToken ct = default)
        => _bybitClient.GetBalanceAsync(asset, ct);

    public Task<bool> TestConnectivityAsync(CancellationToken ct = default)
        => _bybitClient.TestConnectivityAsync(ct);

    public Task<FuturesPosition?> GetPositionAsync(string symbol, CancellationToken ct = default)
        => _bybitClient.GetPositionAsync(symbol, ct);

    public Task<List<FuturesPosition>> GetAllPositionsAsync(CancellationToken ct = default)
        => _bybitClient.GetAllPositionsAsync(ct);

    public Task<bool> SetLeverageAsync(string symbol, int leverage, CancellationToken ct = default)
        => _bybitClient.SetLeverageAsync(symbol, leverage, ct);

    public Task<bool> SetMarginTypeAsync(string symbol, MarginType marginType, CancellationToken ct = default)
        => _bybitClient.SetMarginTypeAsync(symbol, marginType, ct);

    public Task<LeverageInfo> GetLeverageInfoAsync(string symbol, CancellationToken ct = default)
        => _bybitClient.GetLeverageInfoAsync(symbol, ct);

    public Task<decimal> GetLiquidationPriceAsync(string symbol, CancellationToken ct = default)
        => _bybitClient.GetLiquidationPriceAsync(symbol, ct);

    public Task<decimal> GetMarkPriceAsync(string symbol, CancellationToken ct = default)
        => _bybitClient.GetMarkPriceAsync(symbol, ct);

    public Task<bool> SymbolExistsAsync(string symbol, CancellationToken ct = default)
        => _bybitClient.SymbolExistsAsync(symbol, ct);

    public Task<HashSet<string>> GetAllSymbolsAsync(CancellationToken ct = default)
        => _bybitClient.GetAllSymbolsAsync(ct);
}
