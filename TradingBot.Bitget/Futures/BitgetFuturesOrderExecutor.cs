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

        // Bitget defaults to hedge mode where tradeSide is required.
        // Use TradeSide.Open for opening positions in hedge mode.
        var result = await _client.FuturesApiV2.Trading.PlaceOrderAsync(
            productType: productType,
            symbol: symbol,
            marginAsset: marginAsset,
            side: side,
            type: OrderType.Market,
            marginMode: _defaultMarginMode,
            quantity: quantity,
            tradeSide: TradeSide.Open,
            ct: ct);

        // Fallback for one-way mode accounts: if hedge-mode tradeSide is rejected,
        // retry with one-way mode values (BuySingle/SellSingle).
        if (!result.Success && RequiresOneWayModeFallback(result.Error?.Message))
        {
            var oneWayTradeSide = MapOneWayTradeSide(side);

            _logger.Warning(
                "Bitget rejected hedge-mode tradeSide for {Symbol} ({Error}). Retrying with one-way tradeSide \"{TradeSide}\".",
                symbol,
                result.Error?.Message,
                oneWayTradeSide);

            result = await _client.FuturesApiV2.Trading.PlaceOrderAsync(
                productType: productType,
                symbol: symbol,
                marginAsset: marginAsset,
                side: side,
                type: OrderType.Market,
                marginMode: _defaultMarginMode,
                quantity: quantity,
                tradeSide: oneWayTradeSide,
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

        // Get execution price and filled quantity
        decimal avgPrice = 0;
        decimal filledQty = 0;
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
                filledQty = orderResult.Data.QuantityFilled;
                if (avgPrice > 0)
                {
                    _logger.Information("Retrieved actual execution price: {AvgPrice} for order {OrderId} (attempt {Attempt}), Filled: {FilledQty}",
                        avgPrice, orderId, attempt, filledQty);
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

        // Fallback for filled quantity if not retrieved from order details
        if (filledQty == 0)
        {
            filledQty = quantity; // Use normalized quantity as fallback
            _logger.Warning("Could not retrieve filled quantity for order {OrderId}, using requested quantity: {Quantity}",
                orderId, filledQty);
        }

        return new BitgetExecutionResult
        {
            IsAcceptable = true,
            OrderId = orderId,
            FilledQuantity = filledQty,
            ExpectedPrice = avgPrice,
            ActualPrice = avgPrice,
            SlippagePercent = 0,
            SlippageAmount = 0
        };
    }

    public async Task<BitgetExecutionResult> ClosePositionAsync(
        string symbol,
        TradeDirection direction,
        decimal quantity,
        CancellationToken ct = default)
    {
        var productType = BitgetHelpers.ResolveProductType(symbol);
        var marginAsset = BitgetHelpers.ResolveMarginAsset(productType);
        // In Bitget hedge mode, 'side' indicates the POSITION direction, not the trade action.
        // Closing a Long: side=Buy (long side) + tradeSide=Close
        // Closing a Short: side=Sell (short side) + tradeSide=Close
        var side = direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell;
        quantity = await NormalizeQuantityAsync(symbol, quantity, ct);

        _logger.Information("Closing Bitget Futures {Direction} position: {Symbol} x{Quantity}",
            direction, symbol, quantity);

        var result = await _client.FuturesApiV2.Trading.PlaceOrderAsync(
            productType: productType,
            symbol: symbol,
            marginAsset: marginAsset,
            side: side,
            type: OrderType.Market,
            marginMode: _defaultMarginMode,
            quantity: quantity,
            tradeSide: TradeSide.Close,
            ct: ct);

        // Fallback for one-way mode: use opposite side without tradeSide
        if (!result.Success && RequiresOneWayModeFallback(result.Error?.Message))
        {
            var oneWaySide = direction == TradeDirection.Long ? OrderSide.Sell : OrderSide.Buy;
            var oneWayTradeSide = MapOneWayTradeSide(oneWaySide);

            _logger.Warning(
                "Bitget rejected hedge-mode close for {Symbol} ({Error}). Retrying with one-way mode (side={Side}, tradeSide={TradeSide}).",
                symbol,
                result.Error?.Message,
                oneWaySide,
                oneWayTradeSide);

            result = await _client.FuturesApiV2.Trading.PlaceOrderAsync(
                productType: productType,
                symbol: symbol,
                marginAsset: marginAsset,
                side: oneWaySide,
                type: OrderType.Market,
                marginMode: _defaultMarginMode,
                quantity: quantity,
                tradeSide: oneWayTradeSide,
                ct: ct);
        }

        if (!result.Success || result.Data == null)
        {
            _logger.Error("Bitget Futures close position failed: {Error}", result.Error?.Message);
            return new BitgetExecutionResult
            {
                IsAcceptable = false,
                RejectReason = $"Close position failed: {result.Error?.Message}"
            };
        }

        var orderId = long.Parse(result.Data.OrderId);
        _logger.Information("Bitget Futures close position order placed: {OrderId}", orderId);

        // Get execution price and filled quantity
        decimal avgPrice = 0;
        decimal filledQty = 0;
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
                filledQty = orderResult.Data.QuantityFilled;
                if (avgPrice > 0)
                {
                    _logger.Information("Close position execution price: {AvgPrice} for order {OrderId} (attempt {Attempt}), Filled: {FilledQty}",
                        avgPrice, orderId, attempt, filledQty);
                    break;
                }
            }
        }

        if (avgPrice == 0)
        {
            var markPriceResult = await _client.FuturesApiV2.ExchangeData.GetTickerAsync(productType, symbol, ct);
            avgPrice = markPriceResult.Success ? (markPriceResult.Data?.MarkPrice ?? 0m) : 0m;
            _logger.Warning("Could not retrieve close execution price for order {OrderId}, using mark price: {MarkPrice}",
                orderId, avgPrice);
        }

        // Fallback for filled quantity if not retrieved from order details
        if (filledQty == 0)
        {
            filledQty = quantity; // Use normalized quantity as fallback
            _logger.Warning("Could not retrieve filled quantity for close order {OrderId}, using requested quantity: {Quantity}",
                orderId, filledQty);
        }

        return new BitgetExecutionResult
        {
            IsAcceptable = true,
            OrderId = orderId,
            FilledQuantity = filledQty,
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
            FilledQuantity = 0, // Stop loss orders are not filled immediately
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
        var positionSide = direction == TradeDirection.Long ? BitgetPositionSide.Long : BitgetPositionSide.Short;
        quantity = await NormalizeQuantityAsync(symbol, quantity, ct);
        takeProfitPrice = await NormalizePriceAsync(symbol, takeProfitPrice, ct);

        _logger.Information("Placing Bitget Futures take profit: {Symbol} x{Quantity}, TP: {TakeProfitPrice}",
            symbol, quantity, takeProfitPrice);

        // Primary path: dedicated TP/SL trigger order endpoint (supports per-target TP orders).
        // Use hedgeModePositionSide for Bitget's default hedge mode.
        var triggerResult = await _client.FuturesApiV2.Trading.PlaceTpSlOrderAsync(
            productType: productType,
            symbol: symbol,
            marginAsset: marginAsset,
            planType: PlanType.TakeProfit,
            quantity: quantity,
            triggerPrice: takeProfitPrice,
            orderPrice: null,
            triggerPriceType: TriggerPriceType.MarkPrice,
            hedgeModePositionSide: positionSide,
            oneWaySide: null,
            trailingStopRate: null,
            clientOrderId: null,
            ct: ct);

        if (triggerResult.Success && triggerResult.Data != null && long.TryParse(triggerResult.Data.OrderId, out var triggerOrderId))
        {
            _logger.Information("Bitget Futures take profit order placed: {OrderId}", triggerOrderId);
            return new BitgetExecutionResult
            {
                IsAcceptable = true,
                OrderId = triggerOrderId,
                FilledQuantity = 0, // Take profit orders are not filled immediately
                ExpectedPrice = takeProfitPrice,
                ActualPrice = takeProfitPrice,
                SlippagePercent = 0,
                SlippageAmount = 0
            };
        }

        // Fallback for one-way mode: retry with oneWaySide instead of hedgeModePositionSide
        if (!triggerResult.Success && RequiresOneWayModeFallback(triggerResult.Error?.Message))
        {
            _logger.Warning(
                "Bitget rejected hedge-mode TP for {Symbol} ({Error}). Retrying with one-way mode.",
                symbol,
                triggerResult.Error?.Message);

            triggerResult = await _client.FuturesApiV2.Trading.PlaceTpSlOrderAsync(
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
        }

        if (triggerResult.Success && triggerResult.Data != null && long.TryParse(triggerResult.Data.OrderId, out triggerOrderId))
        {
            _logger.Information("Bitget Futures take profit order placed: {OrderId}", triggerOrderId);
            return new BitgetExecutionResult
            {
                IsAcceptable = true,
                OrderId = triggerOrderId,
                FilledQuantity = 0, // Take profit orders are not filled immediately
                ExpectedPrice = takeProfitPrice,
                ActualPrice = takeProfitPrice,
                SlippagePercent = 0,
                SlippageAmount = 0
            };
        }

        if (!RequiresPositionTpSlFallback(triggerResult.Error?.Message))
        {
            _logger.Error("Bitget Futures take profit order failed: {Error}", triggerResult.Error?.Message);
            return new BitgetExecutionResult
            {
                IsAcceptable = false,
                RejectReason = $"Take profit order failed: {triggerResult.Error?.Message}"
            };
        }

        _logger.Warning(
            "Bitget rejected TP trigger order for {Symbol} ({Error}). Retrying via position TP/SL endpoint.",
            symbol,
            triggerResult.Error?.Message);

        var positionResult = await _client.FuturesApiV2.Trading.SetPositionTpSlAsync(
            productType: productType,
            symbol: symbol,
            marginAsset: marginAsset,
            holdSide: positionSide,
            tpTriggerPrice: takeProfitPrice,
            tpTriggerQuantity: quantity,
            tpTriggerType: TriggerPriceType.MarkPrice,
            ct: ct);

        if (!positionResult.Success || positionResult.Data == null)
        {
            _logger.Error("Bitget Futures take profit order failed (position TP/SL): {Error}", positionResult.Error?.Message);
            return new BitgetExecutionResult
            {
                IsAcceptable = false,
                RejectReason = $"Take profit order failed: {positionResult.Error?.Message}"
            };
        }

        var returnedOrderIds = positionResult.Data
            .Select(x => x.OrderId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        _logger.Debug("Bitget position TP/SL response for {Symbol}: returned order ids [{OrderIds}]",
            symbol,
            string.Join(",", returnedOrderIds));

        var orderIdText = returnedOrderIds.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(orderIdText) || !long.TryParse(orderIdText, out var orderId))
        {
            _logger.Error("Bitget Futures take profit order response missing parseable order id for {Symbol}", symbol);
            return new BitgetExecutionResult
            {
                IsAcceptable = false,
                RejectReason = "Take profit order failed: no parseable order id in exchange response"
            };
        }

        _logger.Information("Bitget Futures take profit order placed via position TP/SL: {OrderId}", orderId);

        return new BitgetExecutionResult
        {
            IsAcceptable = true,
            OrderId = orderId,
            FilledQuantity = 0, // Take profit orders are not filled immediately
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

        var success = true;

        // Cancel all regular orders
        var result = await _client.FuturesApiV2.Trading.CancelAllOrdersAsync(
            productType,
            symbol,
            marginAsset: marginAsset,
            ct: ct);

        if (!result.Success)
        {
            _logger.Warning("Failed to cancel regular orders for {Symbol}: {Error}", symbol, result.Error?.Message);
            success = false;
        }

        // Also cancel all trigger orders (TP/SL) — these are separate from regular orders on Bitget
        var triggerResult = await _client.FuturesApiV2.Trading.CancelTriggerOrdersAsync(
            productType,
            planType: null,  // null = cancel all types (SL + TP)
            symbol: symbol,
            marginCoin: marginAsset,
            ct: ct);

        if (!triggerResult.Success)
        {
            _logger.Warning("Failed to cancel trigger orders for {Symbol}: {Error}", symbol, triggerResult.Error?.Message);
            success = false;
        }

        if (success)
            _logger.Information("Cancelled all Bitget Futures orders (regular + trigger) for {Symbol}", symbol);
        else
            _logger.Warning("Partially cancelled orders for {Symbol} — some cancellation requests failed", symbol);

        return success;
    }

    private static bool RequiresOneWayModeFallback(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return false;

        // Bitget rejects hedge-mode tradeSide (Open/Close) when the account is in one-way mode.
        // The error typically mentions "unilateral" or indicates tradeSide is invalid.
        return errorMessage.Contains("unilateral", StringComparison.OrdinalIgnoreCase)
            || (errorMessage.Contains("tradeSide", StringComparison.OrdinalIgnoreCase)
                && errorMessage.Contains("invalid", StringComparison.OrdinalIgnoreCase));
    }

    private static TradeSide MapOneWayTradeSide(OrderSide side)
        => side == OrderSide.Buy ? TradeSide.BuySingle : TradeSide.SellSingle;

    private static bool RequiresPositionTpSlFallback(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return false;

        return errorMessage.Contains("delegateType", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("parameter does not meet", StringComparison.OrdinalIgnoreCase);
    }

}
