using ComplexBot.Models;
using ComplexBot.Services.Trading.SignalFilters;
using ComplexBot.Services.Strategies;

namespace ComplexBot.Services.Trading;

/// <summary>
/// Coordinates multi-timeframe trading for a single symbol.
/// Manages a primary trader and optional filter traders on different timeframes.
/// Example: ADX on 4h (primary) + RSI on 1h (filter) for BTCUSDT.
/// </summary>
public class SymbolTradingUnit : IDisposable
{
    private readonly ISymbolTrader _primaryTrader;
    private readonly List<FilterTraderPair> _filters = new();
    private readonly string _symbol;
    private bool _disposed;
    private Task? _startTask;

    public string Symbol => _symbol;
    public ISymbolTrader PrimaryTrader => _primaryTrader;
    public IReadOnlyList<FilterTraderPair> Filters => _filters.AsReadOnly();

    // Events - forward from primary trader
    public event Action<string>? OnLog;
    public event Action<TradeSignal>? OnSignal;
    public event Action<Trade>? OnTrade;
    public event Action<decimal>? OnEquityUpdate;

    /// <summary>
    /// Creates a trading unit for multi-timeframe analysis.
    /// </summary>
    /// <param name="symbol">The symbol being traded (e.g., BTCUSDT)</param>
    /// <param name="primaryTrader">Primary trader that generates signals</param>
    public SymbolTradingUnit(string symbol, ISymbolTrader primaryTrader)
    {
        _symbol = symbol;
        _primaryTrader = primaryTrader;

        // Forward events from primary trader
        _primaryTrader.OnLog += msg => OnLog?.Invoke(msg);
        _primaryTrader.OnTrade += trade => OnTrade?.Invoke(trade);
        _primaryTrader.OnEquityUpdate += equity => OnEquityUpdate?.Invoke(equity);

        // Intercept signals for filtering
        _primaryTrader.OnSignal += HandlePrimarySignal;
    }

    /// <summary>
    /// Adds a filter trader with its corresponding filter logic.
    /// </summary>
    /// <param name="filterTrader">Trader on different timeframe (e.g., RSI on 1h)</param>
    /// <param name="filter">Filter that evaluates signals based on filter trader state</param>
    public void AddFilter(ISymbolTrader filterTrader, ISignalFilter filter)
    {
        if (filterTrader.Symbol != _symbol)
        {
            throw new ArgumentException($"Filter trader symbol {filterTrader.Symbol} does not match unit symbol {_symbol}");
        }

        _filters.Add(new FilterTraderPair(filterTrader, filter));

        // Forward filter logs with prefix
        filterTrader.OnLog += msg => OnLog?.Invoke($"[Filter:{filter.Name}] {msg}");
    }

    /// <summary>
    /// Starts all traders (primary and filters).
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Log($"Starting trading unit for {_symbol} with {_filters.Count} filter(s)");

        var startTasks = new List<Task>
        {
            Task.Run(() => _primaryTrader.StartAsync(cancellationToken), cancellationToken)
        };

        // Start all filter traders
        foreach (var filterPair in _filters)
        {
            startTasks.Add(Task.Run(() => filterPair.Trader.StartAsync(cancellationToken), cancellationToken));
        }

        _startTask = Task.WhenAll(startTasks);
        _startTask.ContinueWith(
            task => Log($"Trading unit for {_symbol} encountered startup error: {task.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);

        Log($"Trading unit startup initiated for {_symbol}");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops all traders.
    /// </summary>
    public async Task StopAsync()
    {
        Log($"Stopping trading unit for {_symbol}");

        // Stop filter traders first
        foreach (var filterPair in _filters)
        {
            await filterPair.Trader.StopAsync();
        }

        // Stop primary trader
        await _primaryTrader.StopAsync();

        Log($"Trading unit stopped for {_symbol}");
    }

    /// <summary>
    /// Handles signals from primary trader and applies filters.
    /// </summary>
    private void HandlePrimarySignal(TradeSignal signal)
    {
        if (_filters.Count == 0)
        {
            // No filters, pass through directly
            OnSignal?.Invoke(signal);
            return;
        }

        // Apply all filters
        var filterResults = new List<(ISignalFilter Filter, FilterResult Result)>();

        foreach (var filterPair in _filters)
        {
            var filterState = GetFilterState(filterPair.Trader);
            FilterResult result;

            if (!IsFilterStateReady(filterState))
            {
                result = new FilterResult(
                    Approved: true,
                    Reason: "Filter state not ready; skipping filter evaluation",
                    ConfidenceAdjustment: 1.0m);
            }
            else
            {
                result = filterPair.Filter.Evaluate(signal, filterState);
            }

            filterResults.Add((filterPair.Filter, result));

            Log($"Filter '{filterPair.Filter.Name}' ({filterPair.Filter.Mode}): {result.Reason} - " +
                $"Approved={result.Approved}, Confidence={result.ConfidenceAdjustment:F2}");
        }

        // Combine filter results
        var finalDecision = CombineFilterResults(signal, filterResults);

        if (finalDecision.Approved)
        {
            // Modify signal based on confidence adjustment
            var adjustedSignal = ApplyConfidenceAdjustment(signal, finalDecision.ConfidenceAdjustment);
            OnSignal?.Invoke(adjustedSignal);
        }
        else
        {
            Log($"Signal rejected by filters: {finalDecision.Reason}");
        }
    }

    /// <summary>
    /// Combines results from multiple filters to make final decision.
    /// </summary>
    private FilterResult CombineFilterResults(
        TradeSignal signal,
        List<(ISignalFilter Filter, FilterResult Result)> filterResults)
    {
        var confirmFilters = filterResults.Where(f => f.Filter.Mode == FilterMode.Confirm).ToList();
        var vetoFilters = filterResults.Where(f => f.Filter.Mode == FilterMode.Veto).ToList();
        var scoreFilters = filterResults.Where(f => f.Filter.Mode == FilterMode.Score).ToList();

        // Check Confirm filters - ALL must approve
        if (confirmFilters.Any())
        {
            var rejected = confirmFilters.FirstOrDefault(f => !f.Result.Approved);
            if (rejected.Filter != null)
            {
                return new FilterResult(
                    Approved: false,
                    Reason: $"Confirm filter '{rejected.Filter.Name}' rejected: {rejected.Result.Reason}",
                    ConfidenceAdjustment: rejected.Result.ConfidenceAdjustment
                );
            }
        }

        // Check Veto filters - ANY rejection blocks signal
        if (vetoFilters.Any())
        {
            var rejected = vetoFilters.FirstOrDefault(f => !f.Result.Approved);
            if (rejected.Filter != null)
            {
                return new FilterResult(
                    Approved: false,
                    Reason: $"Veto filter '{rejected.Filter.Name}' rejected: {rejected.Result.Reason}",
                    ConfidenceAdjustment: rejected.Result.ConfidenceAdjustment
                );
            }
        }

        // Combine Score filters - multiply confidence adjustments
        decimal combinedConfidence = 1.0m;
        var scoreReasons = new List<string>();

        foreach (var (filter, result) in scoreFilters)
        {
            if (result.ConfidenceAdjustment.HasValue)
            {
                combinedConfidence *= result.ConfidenceAdjustment.Value;
                scoreReasons.Add($"{filter.Name}: {result.ConfidenceAdjustment:F2}x");
            }
        }

        var reason = scoreReasons.Any()
            ? $"Score filters applied: {string.Join(", ", scoreReasons)}"
            : "All filters approved";

        return new FilterResult(
            Approved: true,
            Reason: reason,
            ConfidenceAdjustment: combinedConfidence
        );
    }

    /// <summary>
    /// Adjusts signal confidence based on filter results.
    /// This could modify position sizing, stop loss distance, etc.
    /// </summary>
    private TradeSignal ApplyConfidenceAdjustment(TradeSignal original, decimal? confidenceAdjustment)
    {
        if (!confidenceAdjustment.HasValue || confidenceAdjustment.Value == 1.0m)
        {
            return original;
        }

        // For now, we'll store the confidence in the signal reason
        // The trader can use this to adjust position size
        var adjustedReason = $"{original.Reason} [Confidence: {confidenceAdjustment:F2}x]";

        return new TradeSignal(
            Symbol: original.Symbol,
            Type: original.Type,
            Price: original.Price,
            StopLoss: original.StopLoss,
            TakeProfit: original.TakeProfit,
            Reason: adjustedReason
        );
    }

    /// <summary>
    /// Gets the current state of a filter trader's strategy.
    /// </summary>
    private StrategyState GetFilterState(ISymbolTrader filterTrader)
    {
        return filterTrader.GetStrategyState();
    }

    private static bool IsFilterStateReady(StrategyState filterState)
    {
        return filterState.IndicatorValue.HasValue
            || filterState.LastSignal.HasValue
            || filterState.IsOverbought
            || filterState.IsOversold
            || filterState.IsTrending
            || filterState.CustomValues.Count > 0;
    }

    private void Log(string message)
    {
        OnLog?.Invoke($"[{DateTime.UtcNow:HH:mm:ss}] [Unit:{_symbol}] {message}");
    }

    public void Dispose()
    {
        if (_disposed) return;

        _primaryTrader?.Dispose();
        foreach (var filterPair in _filters)
        {
            filterPair.Trader?.Dispose();
        }

        _disposed = true;
    }
}

/// <summary>
/// Pairs a filter trader with its filter logic.
/// </summary>
/// <param name="Trader">Trader on alternative timeframe</param>
/// <param name="Filter">Filter that evaluates signals based on this trader's state</param>
public record FilterTraderPair(ISymbolTrader Trader, ISignalFilter Filter);
