using TradingBot.Core.Exchanges;
using TradingBot.Core.Models;
using TradingBot.Binance.Futures.Interfaces;
using BinanceExecutionResult = TradingBot.Binance.Common.Models.ExecutionResult;

namespace TradingBot.Binance.Futures.Adapters;

/// <summary>
/// Adapter that wraps Binance IFuturesOrderExecutor and exposes generic IFuturesOrderExecutor interface
/// Converts Binance-specific ExecutionResult to exchange-agnostic Core ExecutionResult
/// </summary>
public class BinanceFuturesOrderExecutorAdapter : Core.Exchanges.IFuturesOrderExecutor
{
    private readonly Interfaces.IFuturesOrderExecutor _binanceExecutor;

    public BinanceFuturesOrderExecutorAdapter(Interfaces.IFuturesOrderExecutor binanceExecutor)
    {
        _binanceExecutor = binanceExecutor;
    }

    public async Task<TradingBot.Core.Models.ExecutionResult> PlaceMarketOrderAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        CancellationToken ct = default)
    {
        var binanceResult = await _binanceExecutor.PlaceMarketOrderAsync(symbol, direction, quantity, ct);
        return ConvertExecutionResult(binanceResult);
    }

    public async Task<TradingBot.Core.Models.ExecutionResult> PlaceLimitOrderAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal price,
        CancellationToken ct = default)
    {
        var binanceResult = await _binanceExecutor.PlaceLimitOrderAsync(symbol, direction, quantity, price, ct);
        return ConvertExecutionResult(binanceResult);
    }

    public async Task<TradingBot.Core.Models.ExecutionResult> PlaceStopLossAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal stopPrice,
        CancellationToken ct = default)
    {
        var binanceResult = await _binanceExecutor.PlaceStopLossAsync(symbol, direction, quantity, stopPrice, ct);
        return ConvertExecutionResult(binanceResult);
    }

    public async Task<TradingBot.Core.Models.ExecutionResult> PlaceTakeProfitAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal takeProfitPrice,
        CancellationToken ct = default)
    {
        var binanceResult = await _binanceExecutor.PlaceTakeProfitAsync(symbol, direction, quantity, takeProfitPrice, ct);
        return ConvertExecutionResult(binanceResult);
    }

    public Task<bool> CancelOrderAsync(string symbol, long orderId, CancellationToken ct = default)
        => _binanceExecutor.CancelOrderAsync(symbol, orderId, ct);

    private static TradingBot.Core.Models.ExecutionResult ConvertExecutionResult(BinanceExecutionResult binanceResult)
    {
        return new TradingBot.Core.Models.ExecutionResult
        {
            Success = binanceResult.IsAcceptable,
            OrderId = binanceResult.OrderId ?? 0,
            FilledQuantity = 0, // Binance result doesn't include this - will be updated via WebSocket
            AveragePrice = binanceResult.ActualPrice,
            ErrorMessage = binanceResult.IsAcceptable ? null : binanceResult.RejectReason
        };
    }
}
