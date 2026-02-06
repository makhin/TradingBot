using TradingBot.Core.Exchanges;
using TradingBot.Core.Models;

namespace TradingBot.Bybit.Futures.Adapters;

/// <summary>
/// Adapter that wraps BybitFuturesOrderExecutor and exposes generic IFuturesOrderExecutor interface
/// Enables exchange-agnostic order execution while preserving Bybit implementation
/// </summary>
public class BybitFuturesOrderExecutorAdapter : IFuturesOrderExecutor
{
    private readonly BybitFuturesOrderExecutor _bybitExecutor;

    public BybitFuturesOrderExecutorAdapter(BybitFuturesOrderExecutor bybitExecutor)
    {
        _bybitExecutor = bybitExecutor;
    }

    public Task<ExecutionResult> PlaceMarketOrderAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        CancellationToken ct = default)
        => _bybitExecutor.PlaceMarketOrderAsync(symbol, direction, quantity, ct);

    public Task<ExecutionResult> PlaceLimitOrderAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal price,
        CancellationToken ct = default)
        => _bybitExecutor.PlaceLimitOrderAsync(symbol, direction, quantity, price, ct);

    public Task<ExecutionResult> PlaceStopLossAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal stopPrice,
        CancellationToken ct = default)
        => _bybitExecutor.PlaceStopLossAsync(symbol, direction, quantity, stopPrice, ct);

    public Task<ExecutionResult> PlaceTakeProfitAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal takeProfitPrice,
        CancellationToken ct = default)
        => _bybitExecutor.PlaceTakeProfitAsync(symbol, direction, quantity, takeProfitPrice, ct);

    public Task<bool> CancelOrderAsync(string symbol, long orderId, CancellationToken ct = default)
        => _bybitExecutor.CancelOrderAsync(symbol, orderId, ct);
}
