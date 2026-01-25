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
    private readonly Dictionary<string, int> _quantityPrecisionCache = new();

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
    /// Normalizes quantity to the valid precision for the symbol
    /// </summary>
    private async Task<decimal> NormalizeQuantityAsync(string symbol, decimal quantity, CancellationToken ct)
    {
        // Check cache first
        if (!_quantityPrecisionCache.TryGetValue(symbol, out var precision))
        {
            // Get exchange info for this symbol
            var exchangeInfo = await _client.UsdFuturesApi.ExchangeData.GetExchangeInfoAsync(ct);
            if (exchangeInfo.Success)
            {
                var symbolInfo = exchangeInfo.Data.Symbols.FirstOrDefault(s => s.Name == symbol);
                if (symbolInfo != null)
                {
                    // Get LOT_SIZE filter to determine precision
                    var lotSizeFilter = symbolInfo.LotSizeFilter;
                    if (lotSizeFilter != null)
                    {
                        // Calculate precision from step size
                        // For example: stepSize = 0.001 => precision = 3
                        var stepSize = lotSizeFilter.StepSize;
                        precision = BitConverter.GetBytes(decimal.GetBits(stepSize)[3])[2];

                        _logger.Debug("Symbol {Symbol}: StepSize={StepSize}, Precision={Precision}",
                            symbol, stepSize, precision);
                    }
                    else
                    {
                        // Default to 8 if no filter found
                        precision = 8;
                        _logger.Warning("No LOT_SIZE filter found for {Symbol}, using default precision {Precision}",
                            symbol, precision);
                    }

                    _quantityPrecisionCache[symbol] = precision;
                }
                else
                {
                    _logger.Warning("Symbol {Symbol} not found in exchange info, using default precision 8", symbol);
                    precision = 8;
                }
            }
            else
            {
                _logger.Warning("Failed to get exchange info: {Error}, using default precision 8",
                    exchangeInfo.Error?.Message);
                precision = 8;
            }
        }

        // Round to the correct precision
        var normalized = Math.Round(quantity, precision);

        if (normalized != quantity)
        {
            _logger.Debug("Normalized quantity for {Symbol}: {Original} => {Normalized} (precision: {Precision})",
                symbol, quantity, normalized, precision);
        }

        return normalized;
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

        // Normalize quantity to valid precision
        quantity = await NormalizeQuantityAsync(symbol, quantity, ct);

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

        _logger.Information("Futures market order placed: {OrderId}, Initial Avg Price: {AvgPrice}, Filled: {FilledQty}",
            order.Id, avgPrice, order.QuantityFilled);

        // For market orders, AveragePrice might be 0 initially
        // Retry multiple times with delay to get actual execution price
        if (avgPrice == 0)
        {
            _logger.Debug("AveragePrice is 0, querying order details for {Symbol} order {OrderId}", symbol, order.Id);

            // Retry up to 3 times with increasing delays (100ms, 200ms, 300ms)
            for (int attempt = 1; attempt <= 3 && avgPrice == 0; attempt++)
            {
                await Task.Delay(attempt * 100, ct);

                var orderResult = await _client.UsdFuturesApi.Trading.GetOrderAsync(symbol, order.Id, ct: ct);
                if (orderResult.Success && orderResult.Data != null)
                {
                    avgPrice = orderResult.Data.AveragePrice;
                    if (avgPrice > 0)
                    {
                        _logger.Information("Retrieved actual execution price: {AvgPrice} for order {OrderId} (attempt {Attempt})",
                            avgPrice, order.Id, attempt);
                        break;
                    }
                    else
                    {
                        _logger.Debug("Attempt {Attempt}: AveragePrice still 0, retrying...", attempt);
                    }
                }
                else
                {
                    _logger.Warning("Failed to retrieve order details for {OrderId} (attempt {Attempt}): {Error}",
                        order.Id, attempt, orderResult.Error?.Message);
                }
            }

            if (avgPrice == 0)
            {
                _logger.Error("Failed to retrieve execution price for order {OrderId} after 3 attempts", order.Id);
            }
        }

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

        // Normalize quantity to valid precision
        quantity = await NormalizeQuantityAsync(symbol, quantity, ct);

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

        // Normalize quantity to valid precision
        quantity = await NormalizeQuantityAsync(symbol, quantity, ct);

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

        // Normalize quantity to valid precision
        quantity = await NormalizeQuantityAsync(symbol, quantity, ct);

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
