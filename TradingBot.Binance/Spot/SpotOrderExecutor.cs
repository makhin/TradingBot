using Binance.Net.Clients;
using Binance.Net.Enums;
using TradingBot.Core.Models;
using TradingBot.Binance.Common;
using TradingBot.Binance.Common.Interfaces;
using TradingBot.Binance.Common.Models;
using Serilog;

namespace TradingBot.Binance.Spot;

/// <summary>
/// Binance Spot market order executor implementation
/// </summary>
public class SpotOrderExecutor : IOrderExecutor
{
    private readonly BinanceRestClient _client;
    private readonly ExecutionValidator _validator;
    private readonly ILogger _logger;

    public SpotOrderExecutor(
        BinanceRestClient client,
        ExecutionValidator validator,
        ILogger? logger = null)
    {
        _client = client;
        _validator = validator;
        _logger = logger ?? Log.ForContext<SpotOrderExecutor>();
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

        _logger.Information("Placing market {Side} order: {Symbol} x{Quantity}", side, symbol, quantity);

        var result = await _client.SpotApi.Trading.PlaceOrderAsync(
            symbol: symbol,
            side: side,
            type: SpotOrderType.Market,
            quantity: quantity,
            ct: ct);

        if (!result.Success)
        {
            _logger.Error("Market order failed: {Error}", result.Error?.Message);
            return new ExecutionResult
            {
                IsAcceptable = false,
                RejectReason = $"Order failed: {result.Error?.Message}"
            };
        }

        var order = result.Data;
        decimal avgPrice = order.AverageFillPrice ?? 0m;

        _logger.Information("Market order filled: {OrderId}, Avg Price: {AvgPrice}, Filled: {FilledQty}",
            order.Id, avgPrice, order.QuantityFilled);

        // Get current market price for validation
        var tickerResult = await _client.SpotApi.ExchangeData.GetTickerAsync(symbol, ct);
        decimal expectedPrice = tickerResult.Success ? tickerResult.Data.LastPrice : avgPrice;

        return _validator.ValidateExecution(expectedPrice, avgPrice, side);
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

        _logger.Information("Placing limit {Side} order: {Symbol} x{Quantity} @ {Price}",
            side, symbol, quantity, price);

        var result = await _client.SpotApi.Trading.PlaceOrderAsync(
            symbol: symbol,
            side: side,
            type: SpotOrderType.Limit,
            quantity: quantity,
            price: price,
            timeInForce: TimeInForce.GoodTillCanceled,
            ct: ct);

        if (!result.Success)
        {
            _logger.Error("Limit order failed: {Error}", result.Error?.Message);
            return new ExecutionResult
            {
                IsAcceptable = false,
                RejectReason = $"Order failed: {result.Error?.Message}"
            };
        }

        _logger.Information("Limit order placed: {OrderId}", result.Data.Id);

        return new ExecutionResult
        {
            IsAcceptable = true,
            ExpectedPrice = price,
            ActualPrice = price,
            SlippagePercent = 0,
            SlippageAmount = 0
        };
    }

    /// <summary>
    /// Places an OCO order with stop-loss and take-profit
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
        // OCO orders on Binance are for closing positions (always opposite side of entry)
        var side = direction == TradeDirection.Long ? OrderSide.Sell : OrderSide.Buy;

        _logger.Information("Placing OCO order: {Symbol} x{Quantity}, TP: {TP}, SL: {SL}, Stop Limit: {SLLimit}",
            symbol, quantity, takeProfitPrice, stopLossPrice, stopLimitPrice);

        var result = await _client.SpotApi.Trading.PlaceOcoOrderAsync(
            symbol: symbol,
            side: side,
            quantity: quantity,
            price: takeProfitPrice,
            stopPrice: stopLossPrice,
            stopLimitPrice: stopLimitPrice,
            stopLimitTimeInForce: TimeInForce.GoodTillCanceled,
            ct: ct);

        if (!result.Success)
        {
            _logger.Error("OCO order failed: {Error}", result.Error?.Message);
            return new ExecutionResult
            {
                IsAcceptable = false,
                RejectReason = $"OCO order failed: {result.Error?.Message}"
            };
        }

        _logger.Information("OCO order placed: List ID {ListId}, Orders count: {OrderCount}",
            result.Data.Id,
            result.Data.Orders.Count());

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
        _logger.Information("Cancelling all orders for {Symbol}", symbol);

        var result = await _client.SpotApi.Trading.CancelAllOrdersAsync(symbol, ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to cancel orders for {Symbol}: {Error}", symbol, result.Error?.Message);
            return false;
        }

        _logger.Information("Cancelled {Count} orders for {Symbol}", result.Data.Count(), symbol);
        return true;
    }

    /// <summary>
    /// Cancels a specific OCO order list
    /// </summary>
    public async Task<bool> CancelOcoOrderAsync(string symbol, long orderListId, CancellationToken ct = default)
    {
        _logger.Information("Cancelling OCO order list {ListId} for {Symbol}", orderListId, symbol);

        var result = await _client.SpotApi.Trading.CancelOcoOrderAsync(symbol, orderListId: orderListId, ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to cancel OCO order {ListId}: {Error}", orderListId, result.Error?.Message);
            return false;
        }

        _logger.Information("Cancelled OCO order list {ListId}", orderListId);
        return true;
    }
}
