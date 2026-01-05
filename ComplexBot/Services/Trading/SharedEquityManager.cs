namespace ComplexBot.Services.Trading;

/// <summary>
/// Manages shared capital pool across multiple trading pairs.
/// Thread-safe for concurrent updates from multiple traders.
/// Tracks equity, allocations, and P&L for portfolio-level risk management.
/// </summary>
public class SharedEquityManager
{
    private decimal _totalEquity;
    private decimal _peakEquity;
    private readonly Dictionary<string, decimal> _symbolAllocations = new();
    private readonly Dictionary<string, decimal> _symbolEquities = new();
    private readonly Dictionary<string, decimal> _symbolRealizedPnL = new();
    private readonly object _lock = new();

    public SharedEquityManager(decimal initialCapital)
    {
        _totalEquity = initialCapital;
        _peakEquity = initialCapital;
    }

    /// <summary>
    /// Gets available capital that is not yet allocated to any symbol.
    /// </summary>
    public decimal GetAvailableCapital()
    {
        lock (_lock)
        {
            var allocated = _symbolAllocations.Values.Sum();
            return Math.Max(0, _totalEquity - allocated);
        }
    }

    /// <summary>
    /// Gets capital allocated to a specific symbol.
    /// </summary>
    public decimal GetAllocatedCapital(string symbol)
    {
        lock (_lock)
        {
            return _symbolAllocations.GetValueOrDefault(symbol, 0);
        }
    }

    /// <summary>
    /// Allocates capital to a symbol (reserves it for trading).
    /// </summary>
    public void AllocateCapital(string symbol, decimal amount)
    {
        lock (_lock)
        {
            _symbolAllocations[symbol] = amount;
            _symbolEquities[symbol] = amount;
        }
    }

    /// <summary>
    /// Releases capital from a symbol (makes it available again).
    /// </summary>
    public void ReleaseCapital(string symbol)
    {
        lock (_lock)
        {
            _symbolAllocations.Remove(symbol);
        }
    }

    /// <summary>
    /// Updates current equity for a symbol (includes unrealized P&L).
    /// </summary>
    public void UpdateSymbolEquity(string symbol, decimal equity)
    {
        lock (_lock)
        {
            _symbolEquities[symbol] = equity;
            RecalculateTotalEquity();
        }

        OnEquityUpdate?.Invoke(symbol, equity);
        OnTotalEquityUpdate?.Invoke(_totalEquity);

        // Alert on significant drawdown
        if (TotalDrawdownPercent >= 10)
            OnDrawdownAlert?.Invoke(TotalDrawdownPercent);
    }

    /// <summary>
    /// Records realized P&L from a closed trade.
    /// </summary>
    public void RecordTradePnL(string symbol, decimal realizedPnL)
    {
        lock (_lock)
        {
            if (!_symbolRealizedPnL.ContainsKey(symbol))
                _symbolRealizedPnL[symbol] = 0;
            _symbolRealizedPnL[symbol] += realizedPnL;
            RecalculateTotalEquity();
        }
    }

    private void RecalculateTotalEquity()
    {
        _totalEquity = _symbolEquities.Values.Sum();
        if (_totalEquity > _peakEquity)
            _peakEquity = _totalEquity;
    }

    /// <summary>
    /// Total portfolio equity across all symbols.
    /// </summary>
    public decimal TotalEquity
    {
        get { lock (_lock) return _totalEquity; }
    }

    /// <summary>
    /// Current drawdown from peak equity as a percentage.
    /// </summary>
    public decimal TotalDrawdownPercent
    {
        get
        {
            lock (_lock)
            {
                if (_peakEquity == 0) return 0;
                return (_peakEquity - _totalEquity) / _peakEquity * 100;
            }
        }
    }

    /// <summary>
    /// Gets a snapshot of the current portfolio state.
    /// </summary>
    public PortfolioSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            var details = _symbolEquities.ToDictionary(
                kvp => kvp.Key,
                kvp => new SymbolEquityInfo(
                    _symbolAllocations.GetValueOrDefault(kvp.Key, 0),
                    kvp.Value,
                    kvp.Value - _symbolAllocations.GetValueOrDefault(kvp.Key, 0),
                    _symbolRealizedPnL.GetValueOrDefault(kvp.Key, 0)
                )
            );

            return new PortfolioSnapshot(
                _totalEquity,
                _peakEquity,
                TotalDrawdownPercent,
                GetAvailableCapital(),
                details
            );
        }
    }

    // Events for monitoring
    public event Action<string, decimal>? OnEquityUpdate;       // symbol, equity
    public event Action<decimal>? OnTotalEquityUpdate;          // total equity
    public event Action<decimal>? OnDrawdownAlert;              // drawdown percent
}

/// <summary>
/// Snapshot of portfolio state at a point in time.
/// </summary>
public record PortfolioSnapshot(
    decimal TotalEquity,
    decimal PeakEquity,
    decimal DrawdownPercent,
    decimal AvailableCapital,
    Dictionary<string, SymbolEquityInfo> SymbolDetails
);

/// <summary>
/// Equity information for a single symbol.
/// </summary>
public record SymbolEquityInfo(
    decimal AllocatedCapital,       // Capital allocated to this symbol
    decimal CurrentEquity,          // Current equity (includes unrealized P&L)
    decimal UnrealizedPnL,          // Unrealized profit/loss
    decimal RealizedPnL             // Realized profit/loss from closed trades
);
