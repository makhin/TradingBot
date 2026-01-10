using Binance.Net.Clients;
using Binance.Net.Enums;
using TradingBot.Core.Models;
using TradingBot.Binance.Common;
using TradingBot.Binance.Common.Interfaces;
using TradingBot.Binance.Common.Models;
using TradingBot.Binance.Futures.Interfaces;
using Serilog;

namespace TradingBot.Binance.Futures;

/// <summary>
/// Binance Futures market order executor implementation
/// </summary>
public class FuturesOrderExecutor : IFuturesOrderExecutor
{
    private readonly BinanceRestClient _client;
    private readonly ExecutionValidator _validator;
    private readonly ILogger _logger;

    public FuturesOrderExecutor(
        BinanceRestClient client,
        ExecutionValidator validator,
        ILogger? logger = null)
    {
        _client = client;
        _validator = validator;
        _logger = logger ?? Log.ForContext<FuturesOrderExecutor>();
    }

    /// <summary>
    /// Places a market order
    /// </summary>
    public async Task<ExecutionResult> PlaceMarketOrderAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        CancellationToken ct = default)
    {
        var side = direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell;

        _logger.Information("Placing Futures market {Side} order: {Symbol} x{Quantity}", side, symbol, quantity);

        var result = await _client.UsdFuturesApi.Trading.PlaceOrderAsync(
            symbol: symbol,
            side: side,
            type: FuturesOrderType.Market,
            quantity: quantity,
            ct: ct);

        if (!result.Success)
        {
            _logger.Error("Futures market order failed: {Error}", result.Error?.Message);
            return new ExecutionResult
            {
                IsAcceptable = false,
                RejectReason = $"Order failed: {result.Error?.Message}"
            };
        }

        var order = result.Data;
        decimal avgPrice = order.AveragePrice;

        _logger.Information("Futures market order filled: {OrderId}, Avg Price: {AvgPrice}, Filled: {FilledQty}",
            order.Id, avgPrice, order.QuantityFilled);

        // Get current mark price for validation
        var markPriceResult = await _client.UsdFuturesApi.ExchangeData.GetMarkPriceAsync(symbol, ct);
        decimal expectedPrice = markPriceResult.Success ? markPriceResult.Data.MarkPrice : avgPrice;

        var executionResult = _validator.ValidateExecution(expectedPrice, avgPrice, side);
        return executionResult with { OrderId = order.Id };
    }

    /// <summary>
    /// Places a limit order
    /// </summary>
    public async Task<ExecutionResult> PlaceLimitOrderAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal price,
        CancellationToken ct = default)
    {
        var side = direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell;

        _logger.Information("Placing Futures limit {Side} order: {Symbol} x{Quantity} @ {Price}",
            side, symbol, quantity, price);

        var result = await _client.UsdFuturesApi.Trading.PlaceOrderAsync(
            symbol: symbol,
            side: side,
            type: FuturesOrderType.Limit,
            quantity: quantity,
            price: price,
            timeInForce: TimeInForce.GoodTillCanceled,
            ct: ct);

        if (!result.Success)
        {
            _logger.Error("Futures limit order failed: {Error}", result.Error?.Message);
            return new ExecutionResult
            {
                IsAcceptable = false,
                RejectReason = $"Order failed: {result.Error?.Message}"
            };
        }

        _logger.Information("Futures limit order placed: {OrderId}", result.Data.Id);

        return new ExecutionResult
        {
            IsAcceptable = true,
            OrderId = result.Data.Id,
            ExpectedPrice = price,
            ActualPrice = price,
            SlippagePercent = 0,
            SlippageAmount = 0
        };
    }

    /// <summary>
    /// Places a stop-loss order (Stop Market)
    /// </summary>
    public async Task<ExecutionResult> PlaceStopLossAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal stopPrice,
        CancellationToken ct = default)
    {
        // Stop loss closes position, so opposite side
        var side = direction == TradeDirection.Long ? OrderSide.Sell : OrderSide.Buy;

        _logger.Information("Placing Futures stop loss: {Symbol} x{Quantity}, Stop: {StopPrice}",
            symbol, quantity, stopPrice);

        var result = await _client.UsdFuturesApi.Trading.PlaceOrderAsync(
            symbol: symbol,
            side: side,
            type: FuturesOrderType.StopMarket,
            quantity: quantity,
            stopPrice: stopPrice,
            timeInForce: TimeInForce.GoodTillCanceled,
            reduceOnly: true,
            ct: ct);

        if (!result.Success)
        {
            _logger.Error("Futures stop loss order failed: {Error}", result.Error?.Message);
            return new ExecutionResult
            {
                IsAcceptable = false,
                RejectReason = $"Stop loss order failed: {result.Error?.Message}"
            };
        }

        _logger.Information("Futures stop loss order placed: {OrderId}", result.Data.Id);

        return new ExecutionResult
        {
            IsAcceptable = true,
            OrderId = result.Data.Id,
            ExpectedPrice = stopPrice,
            ActualPrice = stopPrice,
            SlippagePercent = 0,
            SlippageAmount = 0
        };
    }

    /// <summary>
    /// Places a take-profit order (Take Profit Market)
    /// </summary>
    public async Task<ExecutionResult> PlaceTakeProfitAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal takeProfitPrice,
        CancellationToken ct = default)
    {
        // Take profit closes position, so opposite side
        var side = direction == TradeDirection.Long ? OrderSide.Sell : OrderSide.Buy;

        _logger.Information("Placing Futures take profit: {Symbol} x{Quantity}, TP: {TakeProfitPrice}",
            symbol, quantity, takeProfitPrice);

        var result = await _client.UsdFuturesApi.Trading.PlaceOrderAsync(
            symbol: symbol,
            side: side,
            type: FuturesOrderType.TakeProfitMarket,
            quantity: quantity,
            stopPrice: takeProfitPrice,
            timeInForce: TimeInForce.GoodTillCanceled,
            reduceOnly: true,
            ct: ct);

        if (!result.Success)
        {
            _logger.Error("Futures take profit order failed: {Error}", result.Error?.Message);
            return new ExecutionResult
            {
                IsAcceptable = false,
                RejectReason = $"Take profit order failed: {result.Error?.Message}"
            };
        }

        _logger.Information("Futures take profit order placed: {OrderId}", result.Data.Id);

        return new ExecutionResult
        {
            IsAcceptable = true,
            OrderId = result.Data.Id,
            ExpectedPrice = takeProfitPrice,
            ActualPrice = takeProfitPrice,
            SlippagePercent = 0,
            SlippageAmount = 0
        };
    }

    /// <summary>
    /// Places an OCO-like order pair (stop-loss + take-profit) for Futures
    /// Note: Futures doesn't have native OCO, so we place two separate orders
    /// </summary>
    public async Task<ExecutionResult> PlaceOcoOrderAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal stopLossPrice,
        decimal stopLimitPrice,
        decimal takeProfitPrice,
        CancellationToken ct = default)
    {
        _logger.Information("Placing Futures OCO-like orders: {Symbol} x{Quantity}, TP: {TP}, SL: {SL}",
            symbol, quantity, takeProfitPrice, stopLossPrice);

        // Place stop loss
        var slResult = await PlaceStopLossAsync(symbol, direction, quantity, stopLossPrice, ct);
        if (!slResult.IsAcceptable)
        {
            return slResult;
        }

        // Place take profit
        var tpResult = await PlaceTakeProfitAsync(symbol, direction, quantity, takeProfitPrice, ct);
        if (!tpResult.IsAcceptable)
        {
            // If TP fails, we should cancel the SL order
            _logger.Warning("Take profit failed after stop loss was placed. Consider canceling all orders.");
            return tpResult;
        }

        _logger.Information("Futures OCO-like orders placed successfully");

        return new ExecutionResult
        {
            IsAcceptable = true,
            ExpectedPrice = takeProfitPrice,
            ActualPrice = takeProfitPrice,
            SlippagePercent = 0,
            SlippageAmount = 0
        };
    }

    /// <summary>
    /// Cancels all open orders for a symbol
    /// </summary>
    public async Task<bool> CancelAllOrdersAsync(string symbol, CancellationToken ct = default)
    {
        _logger.Information("Cancelling all Futures orders for {Symbol}", symbol);

        var result = await _client.UsdFuturesApi.Trading.CancelAllOrdersAsync(symbol, ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to cancel Futures orders for {Symbol}: {Error}", symbol, result.Error?.Message);
            return false;
        }

        _logger.Information("Cancelled {Count} Futures orders for {Symbol}", result.Data, symbol);
        return true;
    }

    /// <summary>
    /// Cancels a specific order
    /// </summary>
    public async Task<bool> CancelOrderAsync(string symbol, long orderId, CancellationToken ct = default)
    {
        _logger.Information("Cancelling Futures order {OrderId} for {Symbol}", orderId, symbol);

        var result = await _client.UsdFuturesApi.Trading.CancelOrderAsync(symbol, orderId, ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to cancel Futures order {OrderId}: {Error}", orderId, result.Error?.Message);
            return false;
        }

        _logger.Information("Cancelled Futures order {OrderId}", orderId);
        return true;
    }

    /// <summary>
    /// Note: Futures doesn't have native OCO orders, so this method is not applicable
    /// Use CancelAllOrdersAsync instead
    /// </summary>
    public Task<bool> CancelOcoOrderAsync(string symbol, long orderListId, CancellationToken ct = default)
    {
        _logger.Warning("Futures doesn't support OCO orders. Use CancelAllOrdersAsync instead.");
        return Task.FromResult(false);
    }
}
