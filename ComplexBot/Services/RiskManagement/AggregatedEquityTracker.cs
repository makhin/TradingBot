using System.Collections.Generic;
using System.Linq;

namespace ComplexBot.Services.RiskManagement;

/// <summary>
/// Tracks equity for multiple symbols with aggregation
/// </summary>
public class AggregatedEquityTracker
{
    private readonly Dictionary<string, EquityTracker> _trackers = new();
    private decimal _totalPeakEquity;

    /// <summary>
    /// Gets or creates a tracker for a symbol
    /// </summary>
    public EquityTracker GetTracker(string symbol, decimal initialCapital = 0)
    {
        if (!_trackers.TryGetValue(symbol, out var tracker))
        {
            tracker = new EquityTracker(initialCapital);
            _trackers[symbol] = tracker;
        }
        return tracker;
    }

    /// <summary>
    /// Updates equity for a specific symbol
    /// </summary>
    public void UpdateSymbol(string symbol, decimal equity)
    {
        GetTracker(symbol).Update(equity);
        RecalculateTotals();
    }

    /// <summary>
    /// Total current equity across all symbols
    /// </summary>
    public decimal TotalEquity => _trackers.Values.Sum(t => t.CurrentEquity);

    /// <summary>
    /// Total peak equity (high water mark)
    /// </summary>
    public decimal TotalPeakEquity => _totalPeakEquity;

    /// <summary>
    /// Total drawdown percentage
    /// </summary>
    public decimal TotalDrawdownPercent => _totalPeakEquity > 0
        ? (_totalPeakEquity - TotalEquity) / _totalPeakEquity * 100
        : 0;

    /// <summary>
    /// Gets all symbol equities
    /// </summary>
    public IReadOnlyDictionary<string, decimal> SymbolEquities =>
        _trackers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.CurrentEquity);

    private void RecalculateTotals()
    {
        var totalEquity = TotalEquity;
        if (totalEquity > _totalPeakEquity)
            _totalPeakEquity = totalEquity;
    }
}
