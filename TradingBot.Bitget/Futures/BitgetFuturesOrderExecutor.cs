using Bitget.Net.Clients;
using Bitget.Net.Enums;
using Bitget.Net.Enums.V2;
using Bitget.Net.Objects.Models.V2;
using TradingBot.Core.Models;
using TradingBot.Bitget.Futures.Interfaces;
using Serilog;
using BitgetExecutionResult = TradingBot.Bitget.Futures.Models.ExecutionResult;

namespace TradingBot.Bitget.Futures;

/// <summary>
/// Bitget Futures order executor implementation using JK.Bitget.Net v3.4.0
/// </summary>
public class BitgetFuturesOrderExecutor : IBitgetFuturesOrderExecutor
{
    private readonly BitgetRestClient _client;
    private readonly ILogger _logger;
    private readonly Dictionary<string, (int quantityPrecision, int pricePrecision)> _precisionCache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public BitgetFuturesOrderExecutor(BitgetRestClient client, ILogger? logger = null)
    {
        _client = client;
        _logger = logger ?? Log.ForContext<BitgetFuturesOrderExecutor>();
    }

    private async Task<(int quantityPrecision, int pricePrecision)> GetPrecisionAsync(string symbol, CancellationToken ct)
    {
        if (_precisionCache.TryGetValue(symbol, out var cached))
        {
            return cached;
        }

        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_precisionCache.TryGetValue(symbol, out cached))
            {
                return cached;
            }

            var contractInfo = await _client.FuturesApiV2.ExchangeData.GetContractsAsync(BitgetProductTypeV2.UsdtFutures, symbol, ct);
            if (contractInfo.Success && contractInfo.Data != null)
            {
                // GetContractsAsync returns an array even for single symbol query
                var info = contractInfo.Data.FirstOrDefault();
                if (info != null)
                {
                    var precision = (
                        quantityPrecision: info.QuantityDecimals,
                        pricePrecision: info.PriceDecimals
                    );

                    _precisionCache[symbol] = precision;
                    _logger.Debug("Symbol {Symbol}: QuantityPrecision={QP}, PricePrecision={PP}",
                        symbol, precision.quantityPrecision, precision.pricePrecision);

                    return precision;
                }
            }

            _logger.Warning("Failed to get precision for {Symbol}, using defaults", symbol);
            var defaultPrecision = (quantityPrecision: 3, pricePrecision: 2);
            _precisionCache[symbol] = defaultPrecision;
            return defaultPrecision;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task<decimal> NormalizeQuantityAsync(string symbol, decimal quantity, CancellationToken ct)
    {
        var (quantityPrecision, _) = await GetPrecisionAsync(symbol, ct);
        var normalized = Math.Round(quantity, quantityPrecision);

        if (normalized != quantity)
        {
            _logger.Debug("Normalized quantity for {Symbol}: {Original} => {Normalized} (precision: {Precision})",
                symbol, quantity, normalized, quantityPrecision);
        }

        return normalized;
    }

    private async Task<decimal> NormalizePriceAsync(string symbol, decimal price, CancellationToken ct)
    {
        var (_, pricePrecision) = await GetPrecisionAsync(symbol, ct);
        var normalized = Math.Round(price, pricePrecision);

        if (normalized != price)
        {
            _logger.Debug("Normalized price for {Symbol}: {Original} => {Normalized} (precision: {Precision})",
                symbol, price, normalized, pricePrecision);
        }

        return normalized;
    }

    public async Task<BitgetExecutionResult> PlaceMarketOrderAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        CancellationToken ct = default)
    {
        var side = direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell;
        quantity = await NormalizeQuantityAsync(symbol, quantity, ct);

        _logger.Information("Placing Bitget Futures market {Side} order: {Symbol} x{Quantity}", side, symbol, quantity);

        var result = await _client.FuturesApiV2.Trading.PlaceOrderAsync(
            productType: BitgetProductTypeV2.UsdtFutures,
            symbol: symbol,
            marginAsset: "USDT",
            side: side,
            type: OrderType.Market,
            marginMode: MarginMode.CrossMargin,
            quantity: quantity,
            tradeSide: null,
            ct: ct);

        if (!result.Success && RequiresUnilateralTradeSide(result.Error?.Message))
        {
            _logger.Warning(
                "Bitget rejected market order without tradeSide for {Symbol} ({Error}). Retrying with unilateral tradeSide \"Open\".",
                symbol,
                result.Error?.Message);

            result = await _client.FuturesApiV2.Trading.PlaceOrderAsync(
                productType: BitgetProductTypeV2.UsdtFutures,
                symbol: symbol,
                marginAsset: "USDT",
                side: side,
                type: OrderType.Market,
                marginMode: MarginMode.CrossMargin,
                quantity: quantity,
                tradeSide: TradeSide.Open,
                ct: ct);
        }

        if (!result.Success || result.Data == null)
        {
            _logger.Error("Bitget Futures market order failed: {Error}", result.Error?.Message);
            return new BitgetExecutionResult
            {
                IsAcceptable = false,
                RejectReason = $"Order failed: {result.Error?.Message}"
            };
        }

        var orderId = long.Parse(result.Data.OrderId);
        _logger.Information("Bitget Futures market order placed: {OrderId}", orderId);

        // Get execution price
        decimal avgPrice = 0;
        for (int attempt = 1; attempt <= 3 && avgPrice == 0; attempt++)
        {
            await Task.Delay(attempt * 100, ct);

            var orderResult = await _client.FuturesApiV2.Trading.GetOrderAsync(
                BitgetProductTypeV2.UsdtFutures,
                symbol,
                orderId: result.Data.OrderId,
                ct: ct);

            if (orderResult.Success && orderResult.Data != null)
            {
                avgPrice = orderResult.Data.AveragePrice ?? 0;
                if (avgPrice > 0)
                {
                    _logger.Information("Retrieved actual execution price: {AvgPrice} for order {OrderId} (attempt {Attempt})",
                        avgPrice, orderId, attempt);
                    break;
                }
            }
        }

        if (avgPrice == 0)
        {
            // Fallback to mark price
            var markPriceResult = await _client.FuturesApiV2.ExchangeData.GetTickerAsync(BitgetProductTypeV2.UsdtFutures, symbol, ct);
            avgPrice = markPriceResult.Success ? (markPriceResult.Data?.MarkPrice ?? 0m) : 0m;
            _logger.Warning("Could not retrieve execution price for order {OrderId}, using mark price: {MarkPrice}",
                orderId, avgPrice);
        }

        return new BitgetExecutionResult
        {
            IsAcceptable = true,
            OrderId = orderId,
            ExpectedPrice = avgPrice,
            ActualPrice = avgPrice,
            SlippagePercent = 0,
            SlippageAmount = 0
        };
    }

    public async Task<BitgetExecutionResult> PlaceStopLossAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal stopPrice,
        CancellationToken ct = default)
    {
        var side = direction == TradeDirection.Long ? OrderSide.Sell : OrderSide.Buy;
        quantity = await NormalizeQuantityAsync(symbol, quantity, ct);
        stopPrice = await NormalizePriceAsync(symbol, stopPrice, ct);

        _logger.Information("Placing Bitget Futures stop loss (reduce-only): {Symbol} x{Quantity}, Stop: {StopPrice}",
            symbol, quantity, stopPrice);

        var result = await _client.FuturesApiV2.Trading.PlaceTriggerOrderAsync(
            productType: BitgetProductTypeV2.UsdtFutures,
            symbol: symbol,
            marginAsset: "USDT",
            planType: TriggerPlanType.Normal,
            marginMode: MarginMode.CrossMargin,
            side: side,
            orderType: OrderType.Market,
            quantity: quantity,
            triggerPrice: stopPrice,
            orderPrice: null,
            triggerPriceType: TriggerPriceType.MarkPrice,
            tradeSide: null,
            trailingStopRate: null,
            clientOrderId: null,
            reduceOnly: true,
            takeProfitTriggerPrice: null,
            takeProfitOrderPrice: null,
            takeProfitPriceType: null,
            stopLossTriggerPrice: null,
            stopLossOrderPrice: null,
            stopLossPriceType: null,
            ct: ct);

        if (!result.Success && RequiresUnilateralTradeSide(result.Error?.Message))
        {
            _logger.Warning(
                "Bitget rejected stop loss trigger without tradeSide for {Symbol} ({Error}). Retrying with unilateral tradeSide \"Close\".",
                symbol,
                result.Error?.Message);

            result = await _client.FuturesApiV2.Trading.PlaceTriggerOrderAsync(
                productType: BitgetProductTypeV2.UsdtFutures,
                symbol: symbol,
                marginAsset: "USDT",
                planType: TriggerPlanType.Normal,
                marginMode: MarginMode.CrossMargin,
                side: side,
                orderType: OrderType.Market,
                quantity: quantity,
                triggerPrice: stopPrice,
                orderPrice: null,
                triggerPriceType: TriggerPriceType.MarkPrice,
                tradeSide: TradeSide.Close,
                trailingStopRate: null,
                clientOrderId: null,
                reduceOnly: true,
                takeProfitTriggerPrice: null,
                takeProfitOrderPrice: null,
                takeProfitPriceType: null,
                stopLossTriggerPrice: null,
                stopLossOrderPrice: null,
                stopLossPriceType: null,
                ct: ct);
        }

        if (!result.Success || result.Data == null)
        {
            _logger.Error("Bitget Futures stop loss order failed: {Error}", result.Error?.Message);
            return new BitgetExecutionResult
            {
                IsAcceptable = false,
                RejectReason = $"Stop loss order failed: {result.Error?.Message}"
            };
        }

        var orderId = long.Parse(result.Data.OrderId);
        _logger.Information("Bitget Futures stop loss order placed: {OrderId}", orderId);

        return new BitgetExecutionResult
        {
            IsAcceptable = true,
            OrderId = orderId,
            ExpectedPrice = stopPrice,
            ActualPrice = stopPrice,
            SlippagePercent = 0,
            SlippageAmount = 0
        };
    }

    public async Task<BitgetExecutionResult> PlaceTakeProfitAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        decimal takeProfitPrice,
        CancellationToken ct = default)
    {
        var side = direction == TradeDirection.Long ? OrderSide.Sell : OrderSide.Buy;
        quantity = await NormalizeQuantityAsync(symbol, quantity, ct);
        takeProfitPrice = await NormalizePriceAsync(symbol, takeProfitPrice, ct);

        _logger.Information("Placing Bitget Futures take profit: {Symbol} x{Quantity}, TP: {TakeProfitPrice}",
            symbol, quantity, takeProfitPrice);

        var result = await _client.FuturesApiV2.Trading.PlaceTriggerOrderAsync(
            productType: BitgetProductTypeV2.UsdtFutures,
            symbol: symbol,
            marginAsset: "USDT",
            planType: TriggerPlanType.Normal,
            marginMode: MarginMode.CrossMargin,
            side: side,
            orderType: OrderType.Market,
            quantity: quantity,
            triggerPrice: takeProfitPrice,
            orderPrice: null,
            triggerPriceType: TriggerPriceType.MarkPrice,
            tradeSide: null,
            trailingStopRate: null,
            clientOrderId: null,
            reduceOnly: true,
            takeProfitTriggerPrice: null,
            takeProfitOrderPrice: null,
            takeProfitPriceType: null,
            stopLossTriggerPrice: null,
            stopLossOrderPrice: null,
            stopLossPriceType: null,
            ct: ct);

        if (!result.Success && RequiresUnilateralTradeSide(result.Error?.Message))
        {
            _logger.Warning(
                "Bitget rejected take profit trigger without tradeSide for {Symbol} ({Error}). Retrying with unilateral tradeSide \"Close\".",
                symbol,
                result.Error?.Message);

            result = await _client.FuturesApiV2.Trading.PlaceTriggerOrderAsync(
                productType: BitgetProductTypeV2.UsdtFutures,
                symbol: symbol,
                marginAsset: "USDT",
                planType: TriggerPlanType.Normal,
                marginMode: MarginMode.CrossMargin,
                side: side,
                orderType: OrderType.Market,
                quantity: quantity,
                triggerPrice: takeProfitPrice,
                orderPrice: null,
                triggerPriceType: TriggerPriceType.MarkPrice,
                tradeSide: TradeSide.Close,
                trailingStopRate: null,
                clientOrderId: null,
                reduceOnly: true,
                takeProfitTriggerPrice: null,
                takeProfitOrderPrice: null,
                takeProfitPriceType: null,
                stopLossTriggerPrice: null,
                stopLossOrderPrice: null,
                stopLossPriceType: null,
                ct: ct);
        }

        if (!result.Success || result.Data == null)
        {
            _logger.Error("Bitget Futures take profit order failed: {Error}", result.Error?.Message);
            return new BitgetExecutionResult
            {
                IsAcceptable = false,
                RejectReason = $"Take profit order failed: {result.Error?.Message}"
            };
        }

        var orderId = long.Parse(result.Data.OrderId);
        _logger.Information("Bitget Futures take profit order placed: {OrderId}", orderId);

        return new BitgetExecutionResult
        {
            IsAcceptable = true,
            OrderId = orderId,
            ExpectedPrice = takeProfitPrice,
            ActualPrice = takeProfitPrice,
            SlippagePercent = 0,
            SlippageAmount = 0
        };
    }

    public async Task<bool> CancelOrderAsync(string symbol, long orderId, CancellationToken ct = default)
    {
        _logger.Information("Cancelling Bitget Futures order {OrderId} for {Symbol}", orderId, symbol);

        // Try regular order cancel first
        var result = await _client.FuturesApiV2.Trading.CancelOrderAsync(
            BitgetProductTypeV2.UsdtFutures,
            symbol,
            orderId: orderId.ToString(),
            marginAsset: "USDT",
            ct: ct);

        if (result.Success)
        {
            _logger.Information("Cancelled Bitget Futures order {OrderId}", orderId);
            return true;
        }

        _logger.Warning("Standard cancel failed for {Symbol} order {OrderId}: {Error}. Trying trigger order cancel.",
            symbol, orderId, result.Error?.Message);

        // Try trigger order cancel (plural - takes array of BitgetCancelOrderRequest)
        var cancelRequests = new[]
        {
            new BitgetCancelOrderRequest
            {
                OrderId = orderId.ToString(),
                ClientOrderId = null
            }
        };

        var planResult = await _client.FuturesApiV2.Trading.CancelTriggerOrdersAsync(
            BitgetProductTypeV2.UsdtFutures,
            planType: null,  // null = cancel both SL and TP
            symbol: symbol,
            marginCoin: "USDT",
            orderIds: cancelRequests,
            ct: ct);

        if (!planResult.Success)
        {
            _logger.Error("Failed to cancel Bitget Futures trigger order {OrderId}: {Error}",
                orderId, planResult.Error?.Message);
            return false;
        }

        _logger.Information("Cancelled Bitget Futures trigger order {OrderId}", orderId);
        return true;
    }

    public async Task<bool> CancelAllOrdersAsync(string symbol, CancellationToken ct = default)
    {
        _logger.Information("Cancelling all Bitget Futures orders for {Symbol}", symbol);

        // Cancel all regular orders
        var result = await _client.FuturesApiV2.Trading.CancelAllOrdersAsync(
            BitgetProductTypeV2.UsdtFutures,
            symbol,
            marginAsset: "USDT",
            ct: ct);

        if (!result.Success)
        {
            _logger.Error("Failed to cancel all orders for {Symbol}: {Error}", symbol, result.Error?.Message);
            return false;
        }

        _logger.Information("Cancelled all Bitget Futures orders for {Symbol}", symbol);
        return true;
    }

    private static bool RequiresUnilateralTradeSide(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return false;

        return errorMessage.Contains("unilateral", StringComparison.OrdinalIgnoreCase)
            && errorMessage.Contains("order type", StringComparison.OrdinalIgnoreCase);
    }
}
