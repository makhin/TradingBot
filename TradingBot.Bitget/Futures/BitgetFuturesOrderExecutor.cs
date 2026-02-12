using Bitget.Net.Clients;
using Bitget.Net.Enums;
using Bitget.Net.Enums.V2;
using Bitget.Net.Objects.Models.V2;
using TradingBot.Core.Models;
using TradingBot.Bitget.Common;
using TradingBot.Bitget.Futures.Interfaces;
using Serilog;
using BitgetExecutionResult = TradingBot.Bitget.Futures.Models.ExecutionResult;
using CoreMarginType = TradingBot.Core.Models.MarginType;
using BitgetPositionSide = Bitget.Net.Enums.V2.PositionSide;

namespace TradingBot.Bitget.Futures;

/// <summary>
/// Bitget Futures order executor implementation using JK.Bitget.Net v3.4.0
/// Supports USDT and USDC perpetual markets.
/// </summary>
public class BitgetFuturesOrderExecutor : IBitgetFuturesOrderExecutor
{
    private readonly BitgetRestClient _client;
    private readonly ILogger _logger;
    private readonly MarginMode _defaultMarginMode;
    private readonly Dictionary<string, (int quantityPrecision, int pricePrecision)> _precisionCache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public BitgetFuturesOrderExecutor(
        BitgetRestClient client,
        CoreMarginType defaultMarginType = CoreMarginType.Cross,
        ILogger? logger = null)
    {
        _client = client;
        _defaultMarginMode = BitgetHelpers.MapMarginType(defaultMarginType);
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

            var productType = BitgetHelpers.ResolveProductType(symbol);
            var contractInfo = await _client.FuturesApiV2.ExchangeData.GetContractsAsync(productType, symbol, ct);
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
        var multiplier = Pow10(quantityPrecision);
        var normalized = Math.Truncate(quantity * multiplier) / multiplier;

        if (normalized != quantity)
        {
            _logger.Debug("Normalized quantity for {Symbol}: {Original} => {Normalized} (precision: {Precision})",
                symbol, quantity, normalized, quantityPrecision);
        }

        return normalized;
    }

    private static decimal Pow10(int precision)
    {
        if (precision <= 0)
            return 1m;

        decimal result = 1m;
        for (var i = 0; i < precision; i++)
            result *= 10m;

        return result;
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
        var productType = BitgetHelpers.ResolveProductType(symbol);
        var marginAsset = BitgetHelpers.ResolveMarginAsset(productType);
        var side = direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell;
        quantity = await NormalizeQuantityAsync(symbol, quantity, ct);

        _logger.Information("Placing Bitget Futures market {Side} order: {Symbol} x{Quantity}", side, symbol, quantity);

        var result = await _client.FuturesApiV2.Trading.PlaceOrderAsync(
            productType: productType,
            symbol: symbol,
            marginAsset: marginAsset,
            side: side,
            type: OrderType.Market,
            marginMode: _defaultMarginMode,
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
                productType: productType,
                symbol: symbol,
                marginAsset: marginAsset,
                side: side,
                type: OrderType.Market,
                marginMode: _defaultMarginMode,
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
                productType,
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
            var markPriceResult = await _client.FuturesApiV2.ExchangeData.GetTickerAsync(productType, symbol, ct);
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
        var productType = BitgetHelpers.ResolveProductType(symbol);
        var marginAsset = BitgetHelpers.ResolveMarginAsset(productType);
        quantity = await NormalizeQuantityAsync(symbol, quantity, ct);
        stopPrice = await NormalizePriceAsync(symbol, stopPrice, ct);

        _logger.Information("Placing Bitget Futures stop loss (reduce-only): {Symbol} x{Quantity}, Stop: {StopPrice}",
            symbol, quantity, stopPrice);

        var positionSide = direction == TradeDirection.Long ? BitgetPositionSide.Long : BitgetPositionSide.Short;

        // Use position TP/SL endpoint for stop-loss placement.
        // Bitget can reject /place-tpsl-order with delegateType validation errors for some account modes.
        var result = await _client.FuturesApiV2.Trading.SetPositionTpSlAsync(
            productType: productType,
            symbol: symbol,
            marginAsset: marginAsset,
            holdSide: positionSide,
            slTriggerPrice: stopPrice,
            slTriggerQuantity: quantity,
            slTriggerType: TriggerPriceType.MarkPrice,
            ct: ct);

        if (!result.Success || result.Data == null)
        {
            _logger.Error("Bitget Futures stop loss order failed: {Error}", result.Error?.Message);
            return new BitgetExecutionResult
            {
                IsAcceptable = false,
                RejectReason = $"Stop loss order failed: {result.Error?.Message}"
            };
        }

        var returnedOrderIds = result.Data
            .Select(x => x.OrderId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        _logger.Debug("Bitget position TP/SL response for {Symbol}: returned order ids [{OrderIds}]",
            symbol,
            string.Join(",", returnedOrderIds));

        var orderIdText = returnedOrderIds.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(orderIdText) || !long.TryParse(orderIdText, out var orderId))
        {
            _logger.Error("Bitget Futures stop loss order response missing parseable order id for {Symbol}", symbol);
            return new BitgetExecutionResult
            {
                IsAcceptable = false,
                RejectReason = "Stop loss order failed: no parseable order id in exchange response"
            };
        }

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
        var productType = BitgetHelpers.ResolveProductType(symbol);
        var marginAsset = BitgetHelpers.ResolveMarginAsset(productType);
        var side = direction == TradeDirection.Long ? OrderSide.Sell : OrderSide.Buy;
        quantity = await NormalizeQuantityAsync(symbol, quantity, ct);
        takeProfitPrice = await NormalizePriceAsync(symbol, takeProfitPrice, ct);

        _logger.Information("Placing Bitget Futures take profit: {Symbol} x{Quantity}, TP: {TakeProfitPrice}",
            symbol, quantity, takeProfitPrice);

        var result = await _client.FuturesApiV2.Trading.PlaceTpSlOrderAsync(
            productType: productType,
            symbol: symbol,
            marginAsset: marginAsset,
            planType: PlanType.TakeProfit,
            quantity: quantity,
            triggerPrice: takeProfitPrice,
            orderPrice: null,
            triggerPriceType: TriggerPriceType.MarkPrice,
            hedgeModePositionSide: null,
            oneWaySide: side,
            trailingStopRate: null,
            clientOrderId: null,
            ct: ct);

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
        var productType = BitgetHelpers.ResolveProductType(symbol);
        var marginAsset = BitgetHelpers.ResolveMarginAsset(productType);

        // Try regular order cancel first
        var result = await _client.FuturesApiV2.Trading.CancelOrderAsync(
            productType,
            symbol,
            orderId: orderId.ToString(),
            marginAsset: marginAsset,
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
            productType,
            planType: null,  // null = cancel both SL and TP
            symbol: symbol,
            marginCoin: marginAsset,
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
        var productType = BitgetHelpers.ResolveProductType(symbol);
        var marginAsset = BitgetHelpers.ResolveMarginAsset(productType);

        // Cancel all regular orders
        var result = await _client.FuturesApiV2.Trading.CancelAllOrdersAsync(
            productType,
            symbol,
            marginAsset: marginAsset,
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
