using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Binance.Net.Clients;
using TradingBot.Core.Models;
using TradingBot.Core.State;
using Serilog;

namespace TradingBot.Binance.Reconciliation;

/// <summary>
/// Reconciles saved bot state with actual exchange state
/// </summary>
public class SpotStateReconciler
{
    private readonly BinanceRestClient _client;
    private readonly ILogger _logger;

    public SpotStateReconciler(BinanceRestClient client, ILogger? logger = null)
    {
        _client = client;
        _logger = logger ?? Log.ForContext<SpotStateReconciler>();
    }

    /// <summary>
    /// Reconciles saved state with exchange reality
    /// </summary>
    public async Task<StateReconciliationResult> ReconcileAsync(
        BotState savedState,
        string symbol,
        CancellationToken ct = default)
    {
        _logger.Information("🔍 Reconciling state with Binance for {Symbol}...", symbol);

        var result = new StateReconciliationResult();

        try
        {
            // 1. Check asset balance for positions
            var exchangePositions = await QueryExchangePositionsAsync(symbol, ct);

            foreach (var savedPos in savedState.OpenPositions)
            {
                var match = exchangePositions.FirstOrDefault(ep =>
                    ep.Symbol == savedPos.Symbol &&
                    Math.Abs(ep.Quantity - savedPos.RemainingQuantity) < 0.0001m);

                if (match != null)
                {
                    result.PositionsConfirmed.Add(savedPos);
                    _logger.Debug("✅ Position confirmed: {Symbol} {Qty:F5}",
                        savedPos.Symbol, savedPos.RemainingQuantity);
                }
                else
                {
                    var actualQty = exchangePositions
                        .FirstOrDefault(ep => ep.Symbol == savedPos.Symbol)?.Quantity ?? 0;
                    result.PositionsMismatch.Add((savedPos, actualQty));
                    _logger.Warning("⚠️ Position mismatch: {Symbol}. Expected {Expected:F5}, Found {Actual:F5}",
                        savedPos.Symbol, savedPos.RemainingQuantity, actualQty);
                }
            }

            // 2. Check OCO orders
            var activeOcos = await QueryActiveOcoOrdersAsync(symbol, ct);

            foreach (var savedOco in savedState.ActiveOcoOrders)
            {
                var isActive = activeOcos.Any(oco => oco.OrderListId == savedOco.OrderListId);

                if (isActive)
                {
                    result.OcoOrdersActive.Add(savedOco);
                    _logger.Debug("✅ OCO order active: {Symbol} #{OrderListId}",
                        savedOco.Symbol, savedOco.OrderListId);
                }
                else
                {
                    result.OcoOrdersMissing.Add(savedOco);
                    _logger.Warning("⚠️ OCO order missing: {Symbol} #{OrderListId} (likely filled/cancelled)",
                        savedOco.Symbol, savedOco.OrderListId);
                }
            }

            _logger.Information("Reconciliation complete: {ConfirmedPos}/{TotalPos} positions, {ActiveOco}/{TotalOco} OCO orders",
                result.PositionsConfirmed.Count, savedState.OpenPositions.Count,
                result.OcoOrdersActive.Count, savedState.ActiveOcoOrders.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during state reconciliation");
            throw;
        }

        return result;
    }

    /// <summary>
    /// Queries exchange for actual asset positions (spot balances)
    /// </summary>
    public async Task<List<SavedPosition>> QueryExchangePositionsAsync(
        string symbol,
        CancellationToken ct = default)
    {
        try
        {
            // Get account balances
            var balanceResult = await _client.SpotApi.Account.GetAccountInfoAsync(ct: ct);
            if (!balanceResult.Success)
            {
                _logger.Error("Failed to get account info: {Error}", balanceResult.Error?.Message);
                return new List<SavedPosition>();
            }

            // Extract base asset from symbol (e.g., "BTC" from "BTCUSDT")
            var baseAsset = symbol.Replace("USDT", "").Replace("BUSD", "").Replace("USDC", "");
            var balance = balanceResult.Data.Balances.FirstOrDefault(b => b.Asset == baseAsset);

            if (balance == null || balance.Available <= 0)
            {
                _logger.Debug("No balance found for {Asset}", baseAsset);
                return new List<SavedPosition>();
            }

            // Get current price
            var priceResult = await _client.SpotApi.ExchangeData.GetPriceAsync(symbol, ct: ct);
            var currentPrice = priceResult.Success ? priceResult.Data.Price : 0;

            _logger.Debug("Found balance: {Asset} = {Available:F5}, Current price: {Price:F2}",
                baseAsset, balance.Available, currentPrice);

            // Return position (note: entry price/SL/TP are unknown from exchange alone)
            return new List<SavedPosition>
            {
                new()
                {
                    Symbol = symbol,
                    Quantity = balance.Available,
                    RemainingQuantity = balance.Available,
                    CurrentPrice = currentPrice,
                    // Entry price, SL, TP are unknown - will need state or user input
                    Direction = SignalType.Buy, // Assume long for spot
                    EntryPrice = 0,
                    StopLoss = 0,
                    TakeProfit = 0,
                    EntryTime = DateTime.UtcNow,
                    TradeId = 0,
                    RiskAmount = 0,
                    BreakevenMoved = false
                }
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error querying exchange positions for {Symbol}", symbol);
            return new List<SavedPosition>();
        }
    }

    /// <summary>
    /// Queries exchange for active OCO orders
    /// </summary>
    public async Task<List<SavedOcoOrder>> QueryActiveOcoOrdersAsync(
        string symbol,
        CancellationToken ct = default)
    {
        try
        {
            // Get all open orders for symbol
            var openOrdersResult = await _client.SpotApi.Trading.GetOpenOrdersAsync(symbol, ct: ct);
            if (!openOrdersResult.Success)
            {
                _logger.Error("Failed to get open orders: {Error}", openOrdersResult.Error?.Message);
                return new List<SavedOcoOrder>();
            }

            // Filter OCO orders (orderListId > 0)
            var ocoOrders = openOrdersResult.Data
                .Where(o => o.OrderListId > 0)
                .GroupBy(o => o.OrderListId)
                .Select(g => new SavedOcoOrder
                {
                    Symbol = symbol,
                    OrderListId = g.Key
                })
                .ToList();

            _logger.Debug("Found {Count} active OCO orders for {Symbol}", ocoOrders.Count, symbol);

            return ocoOrders;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error querying OCO orders for {Symbol}", symbol);
            return new List<SavedOcoOrder>();
        }
    }
}
