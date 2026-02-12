using TradingBot.Core.Exchanges;
using TradingBot.Core.Models;
using TradingBot.Bitget.Futures.Interfaces;
using BitgetExecutionResult = TradingBot.Bitget.Futures.Models.ExecutionResult;

namespace TradingBot.Bitget.Futures.Adapters;

/// <summary>
/// Adapter that wraps Bitget IBitgetFuturesOrderExecutor and exposes generic IFuturesOrderExecutor interface
/// Converts Bitget-specific ExecutionResult to exchange-agnostic Core ExecutionResult
/// </summary>
public class BitgetFuturesOrderExecutorAdapter : Core.Exchanges.IFuturesOrderExecutor
{
    private readonly IBitgetFuturesOrderExecutor _bitgetExecutor;

    public BitgetFuturesOrderExecutorAdapter(IBitgetFuturesOrderExecutor bitgetExecutor)
    {
        _bitgetExecutor = bitgetExecutor;
    }

    public async Task<TradingBot.Core.Models.ExecutionResult> PlaceMarketOrderAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        CancellationToken ct = default)
    {
        var bitgetResult = await _bitgetExecutor.PlaceMarketOrderAsync(symbol, direction, quantity, ct);
        return ConvertExecutionResult(bitgetResult);
    }

    public async Task<TradingBot.Core.Models.ExecutionResult> PlaceLimitOrderAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal price,
        CancellationToken ct = default)
    {
        // Bitget doesn't have a separate PlaceLimitOrderAsync in the interface
        // For now, return an error indicating it's not implemented
        return new TradingBot.Core.Models.ExecutionResult
        {
            Success = false,
            ErrorMessage = "Limit orders not implemented for Bitget"
        };
    }

    public async Task<TradingBot.Core.Models.ExecutionResult> PlaceStopLossAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal stopPrice,
        CancellationToken ct = default)
    {
        var bitgetResult = await _bitgetExecutor.PlaceStopLossAsync(symbol, direction, quantity, stopPrice, ct);
        return ConvertExecutionResult(bitgetResult);
    }

    public async Task<TradingBot.Core.Models.ExecutionResult> PlaceTakeProfitAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal takeProfitPrice,
        CancellationToken ct = default)
    {
        var bitgetResult = await _bitgetExecutor.PlaceTakeProfitAsync(symbol, direction, quantity, takeProfitPrice, ct);
        return ConvertExecutionResult(bitgetResult);
    }

    public async Task<TradingBot.Core.Models.ExecutionResult> ClosePositionAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        CancellationToken ct = default)
    {
        var bitgetResult = await _bitgetExecutor.ClosePositionAsync(symbol, direction, quantity, ct);
        return ConvertExecutionResult(bitgetResult);
    }

    public Task<bool> CancelOrderAsync(string symbol, long orderId, CancellationToken ct = default)
        => _bitgetExecutor.CancelOrderAsync(symbol, orderId, ct);

    private static TradingBot.Core.Models.ExecutionResult ConvertExecutionResult(BitgetExecutionResult bitgetResult)
    {
        return new TradingBot.Core.Models.ExecutionResult
        {
            Success = bitgetResult.IsAcceptable,
            OrderId = bitgetResult.OrderId,
            FilledQuantity = 0,
            AveragePrice = bitgetResult.ActualPrice,
            ErrorMessage = bitgetResult.IsAcceptable ? null : bitgetResult.RejectReason
        };
    }
}
