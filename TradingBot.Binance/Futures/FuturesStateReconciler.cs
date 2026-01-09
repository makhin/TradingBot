using TradingBot.Core.Models;
using TradingBot.Core.State;
using TradingBot.Binance.Futures.Interfaces;
using TradingBot.Binance.Futures.Models;
using Serilog;

namespace TradingBot.Binance.Futures;

/// <summary>
/// Reconciles saved state with actual Futures exchange state
/// </summary>
public class FuturesStateReconciler
{
    private readonly IBinanceFuturesClient _client;
    private readonly ILogger _logger;

    public FuturesStateReconciler(IBinanceFuturesClient client, ILogger? logger = null)
    {
        _client = client;
        _logger = logger ?? Log.ForContext<FuturesStateReconciler>();
    }

    /// <summary>
    /// Reconciles saved position with actual exchange position
    /// </summary>
    public async Task<FuturesReconciliationResult> ReconcilePositionAsync(
        SavedPosition? savedPosition,
        string symbol,
        CancellationToken ct = default)
    {
        _logger.Information("Reconciling position for {Symbol}", symbol);

        var exchangePosition = await _client.GetPositionAsync(symbol, ct);

        // No saved position and no exchange position = consistent
        if (savedPosition == null && exchangePosition == null)
        {
            _logger.Information("No position found (saved or exchange) - state is consistent");
            return new FuturesReconciliationResult
            {
                IsConsistent = true,
                Message = "No position found - state is consistent"
            };
        }

        // Saved position exists but no exchange position = inconsistent
        if (savedPosition != null && exchangePosition == null)
        {
            _logger.Warning("Saved position exists but no exchange position found");
            return new FuturesReconciliationResult
            {
                IsConsistent = false,
                Message = "Saved position exists but no exchange position found",
                Discrepancies = new List<string>
                {
                    $"Saved: {savedPosition.Direction} {savedPosition.Quantity} @ {savedPosition.EntryPrice}",
                    "Exchange: No position"
                },
                RecommendedAction = "Clear saved position state",
                SavedPosition = savedPosition
            };
        }

        // No saved position but exchange position exists = inconsistent
        if (savedPosition == null && exchangePosition != null)
        {
            _logger.Warning("Exchange position exists but no saved position found");
            return new FuturesReconciliationResult
            {
                IsConsistent = false,
                Message = "Exchange position exists but no saved position found",
                Discrepancies = new List<string>
                {
                    "Saved: No position",
                    $"Exchange: {exchangePosition.Side} {exchangePosition.Quantity} @ {exchangePosition.EntryPrice}"
                },
                RecommendedAction = "Save current exchange position or close it",
                ExchangePosition = exchangePosition
            };
        }

        // Both positions exist - check if they match
        var savedDirection = savedPosition!.Direction == SignalType.Buy
            ? Models.PositionSide.Long
            : Models.PositionSide.Short;

        var discrepancies = new List<string>();

        if (savedDirection != exchangePosition!.Side)
        {
            discrepancies.Add($"Direction mismatch: Saved={savedPosition.Direction}, Exchange={exchangePosition.Side}");
        }

        var quantityDiff = Math.Abs(savedPosition.Quantity - exchangePosition.Quantity);
        var quantityTolerance = exchangePosition.Quantity * 0.01m; // 1% tolerance

        if (quantityDiff > quantityTolerance)
        {
            discrepancies.Add($"Quantity mismatch: Saved={savedPosition.Quantity}, Exchange={exchangePosition.Quantity}");
        }

        var priceDiff = Math.Abs(savedPosition.EntryPrice - exchangePosition.EntryPrice);
        var priceTolerance = exchangePosition.EntryPrice * 0.01m; // 1% tolerance

        if (priceDiff > priceTolerance)
        {
            discrepancies.Add($"Entry price mismatch: Saved={savedPosition.EntryPrice}, Exchange={exchangePosition.EntryPrice}");
        }

        if (discrepancies.Any())
        {
            _logger.Warning("Position discrepancies found: {Discrepancies}", string.Join("; ", discrepancies));
            return new FuturesReconciliationResult
            {
                IsConsistent = false,
                Message = "Position discrepancies detected",
                Discrepancies = discrepancies,
                RecommendedAction = "Update saved position with exchange data",
                SavedPosition = savedPosition,
                ExchangePosition = exchangePosition
            };
        }

        _logger.Information("Position state is consistent");
        return new FuturesReconciliationResult
        {
            IsConsistent = true,
            Message = "Position state is consistent",
            SavedPosition = savedPosition,
            ExchangePosition = exchangePosition
        };
    }

    /// <summary>
    /// Reconciles all saved positions with exchange positions
    /// </summary>
    public async Task<List<FuturesReconciliationResult>> ReconcileAllPositionsAsync(
        List<SavedPosition> savedPositions,
        CancellationToken ct = default)
    {
        _logger.Information("Reconciling all positions");

        var exchangePositions = await _client.GetAllPositionsAsync(ct);
        var results = new List<FuturesReconciliationResult>();

        // Check all saved positions
        foreach (var savedPos in savedPositions)
        {
            var result = await ReconcilePositionAsync(savedPos, savedPos.Symbol, ct);
            results.Add(result);
        }

        // Check for exchange positions not in saved state
        var savedSymbols = savedPositions.Select(p => p.Symbol).ToHashSet();
        var exchangeOnlyPositions = exchangePositions
            .Where(ep => !savedSymbols.Contains(ep.Symbol))
            .ToList();

        foreach (var exchangePos in exchangeOnlyPositions)
        {
            _logger.Warning("Found exchange position not in saved state: {Symbol}", exchangePos.Symbol);
            results.Add(new FuturesReconciliationResult
            {
                IsConsistent = false,
                Message = $"Exchange position found without saved state: {exchangePos.Symbol}",
                Discrepancies = new List<string>
                {
                    $"Exchange: {exchangePos.Side} {exchangePos.Quantity} @ {exchangePos.EntryPrice}"
                },
                RecommendedAction = "Save exchange position or close it",
                ExchangePosition = exchangePos
            });
        }

        var inconsistentCount = results.Count(r => !r.IsConsistent);
        _logger.Information("Reconciliation complete: {Total} positions checked, {Inconsistent} inconsistencies found",
            results.Count, inconsistentCount);

        return results;
    }
}

/// <summary>
/// Result of state reconciliation
/// </summary>
public record FuturesReconciliationResult
{
    public required bool IsConsistent { get; init; }
    public required string Message { get; init; }
    public List<string>? Discrepancies { get; init; }
    public string? RecommendedAction { get; init; }
    public SavedPosition? SavedPosition { get; init; }
    public FuturesPosition? ExchangePosition { get; init; }
}
