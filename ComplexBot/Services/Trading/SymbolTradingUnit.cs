using Binance.Net.Enums;
using ComplexBot.Models;
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
    private readonly List<FilterTraderPair> _traderFilters = new();
    private readonly List<FilterStrategyPair> _strategyFilters = new();
    private readonly string _symbol;
    private bool _disposed;
    private Task? _startTask;

    public string Symbol => _symbol;
    public ISymbolTrader PrimaryTrader => _primaryTrader;
    public IReadOnlyList<FilterTraderPair> Filters => _traderFilters.AsReadOnly();
    public IReadOnlyList<FilterStrategyPair> StrategyFilters => _strategyFilters.AsReadOnly();

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

        _traderFilters.Add(new FilterTraderPair(filterTrader, filter));

        // Forward filter logs with prefix
        filterTrader.OnLog += msg => OnLog?.Invoke($"[Filter:{filter.Name}] {msg}");
    }

    /// <summary>
    /// Adds a filter strategy that is updated externally by interval.
    /// </summary>
    /// <param name="strategy">Filter strategy instance</param>
    /// <param name="interval">Interval associated with the filter strategy</param>
    /// <param name="filter">Filter that evaluates signals based on strategy state</param>
    public void AddFilter(IStrategy strategy, KlineInterval interval, ISignalFilter filter)
    {
        _strategyFilters.Add(new FilterStrategyPair(strategy, interval, filter));
    }

    /// <summary>
    /// Updates filter strategies with a candle for the specified interval.
    /// </summary>
    public void UpdateFilterStrategy(KlineInterval interval, Candle candle)
    {
        foreach (var filterPair in _strategyFilters.Where(f => f.Interval == interval))
        {
            filterPair.Strategy.Analyze(candle, 0, _symbol);
        }
    }

    /// <summary>
    /// Starts all traders (primary and filters).
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Log($"Starting trading unit for {_symbol} with {_traderFilters.Count + _strategyFilters.Count} filter(s)");

        var startTasks = new List<Task>
        {
            Task.Run(() => _primaryTrader.StartAsync(cancellationToken), cancellationToken)
        };

        // Start all filter traders
        foreach (var filterPair in _traderFilters)
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
        foreach (var filterPair in _traderFilters)
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
        if (_traderFilters.Count == 0 && _strategyFilters.Count == 0)
        {
            // No filters, pass through directly
            OnSignal?.Invoke(signal);
            return;
        }

        // Apply all filters
        var filterResults = new List<(ISignalFilter Filter, FilterResult Result)>();

        foreach (var filterPair in _traderFilters)
        {
            var filterState = filterPair.Trader.GetStrategyState();
            FilterResult result;

            if (!SignalFilterEvaluator.IsFilterStateReady(filterState))
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

        foreach (var filterPair in _strategyFilters)
        {
            var filterState = filterPair.Strategy.GetCurrentState();
            FilterResult result;

            if (!SignalFilterEvaluator.IsFilterStateReady(filterState))
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

            Log($"Filter '{filterPair.Filter.Name}' ({filterPair.Filter.Mode}) [{filterPair.Interval}]: {result.Reason} - " +
                $"Approved={result.Approved}, Confidence={result.ConfidenceAdjustment:F2}");
        }

        // Combine filter results
        var finalDecision = SignalFilterEvaluator.CombineFilterResults(filterResults);

        if (finalDecision.Approved)
        {
            // Modify signal based on confidence adjustment
            var adjustedSignal = SignalFilterEvaluator.ApplyConfidenceAdjustment(signal, finalDecision.ConfidenceAdjustment);
            OnSignal?.Invoke(adjustedSignal);
        }
        else
        {
            Log($"Signal rejected by filters: {finalDecision.Reason}");
        }
    }

    private void Log(string message)
    {
        OnLog?.Invoke($"[{DateTime.UtcNow:HH:mm:ss}] [Unit:{_symbol}] {message}");
    }

    public void Dispose()
    {
        if (_disposed) return;

        _primaryTrader?.Dispose();
        foreach (var filterPair in _traderFilters)
        {
            filterPair.Trader?.Dispose();
        }

        _disposed = true;
    }
}
