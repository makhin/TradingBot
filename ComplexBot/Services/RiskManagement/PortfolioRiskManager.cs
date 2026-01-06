using ComplexBot.Models;
using Serilog;

namespace ComplexBot.Services.RiskManagement;

public class PortfolioRiskManager
{
    private readonly Dictionary<string, RiskManager> _symbolManagers = new();
    private readonly PortfolioRiskSettings _settings;
    private readonly Dictionary<string, string[]> _correlationGroups;
    private readonly AggregatedEquityTracker _equityTracker = new();
    private readonly ILogger _logger;

    public PortfolioRiskManager(
        PortfolioRiskSettings settings,
        Dictionary<string, string[]>? correlationGroups = null,
        ILogger? logger = null)
    {
        _settings = settings;
        _logger = logger ?? Log.ForContext<PortfolioRiskManager>();
        _correlationGroups = BuildCorrelationGroups(correlationGroups);
    }

    private Dictionary<string, string[]> BuildCorrelationGroups(Dictionary<string, string[]>? correlationGroups)
    {
        if (correlationGroups == null || correlationGroups.Count == 0)
        {
            _logger.Warning("⚠️ Correlation groups are not configured. Symbols will be treated as independent.");
            return new Dictionary<string, string[]>();
        }

        return new Dictionary<string, string[]>(correlationGroups);
    }

    public void RegisterSymbol(string symbol, RiskManager riskManager)
    {
        _symbolManagers[symbol] = riskManager;
    }

    public decimal GetCorrelatedRisk(string symbol)
    {
        // Find the correlation group for this symbol
        var group = _correlationGroups
            .FirstOrDefault(g => g.Value.Contains(symbol));

        if (group.Key == null)
        {
            // Symbol not in any group - treat as independent
            return 0;
        }

        // Sum up portfolio heat across all symbols in the group
        decimal totalRisk = 0;
        foreach (var correlatedSymbol in group.Value)
        {
            if (_symbolManagers.TryGetValue(correlatedSymbol, out var manager))
            {
                totalRisk += manager.PortfolioHeat;
            }
        }

        return totalRisk;
    }

    public bool CanOpenPosition(string symbol)
    {
        // 1. Check total portfolio drawdown
        var totalDrawdown = GetTotalDrawdownPercent();
        if (totalDrawdown >= _settings.MaxTotalDrawdownPercent)
        {
            _logger.Warning("⛔ Portfolio drawdown exceeded: {TotalDrawdown:F2}% >= {MaxTotalDrawdown:F2}%",
                totalDrawdown,
                _settings.MaxTotalDrawdownPercent);
            return false;
        }

        // 2. Check correlated risk
        var correlatedRisk = GetCorrelatedRisk(symbol);
        if (correlatedRisk >= _settings.MaxCorrelatedRiskPercent)
        {
            var groupName = _correlationGroups
                .FirstOrDefault(g => g.Value.Contains(symbol)).Key ?? "Unknown";
            _logger.Warning("⛔ Correlated risk too high for {Symbol} (group: {GroupName}): {CorrelatedRisk:F2}% >= {MaxCorrelatedRisk:F2}%",
                symbol,
                groupName,
                correlatedRisk,
                _settings.MaxCorrelatedRiskPercent);
            return false;
        }

        // 3. Check max concurrent positions
        var openPositionsCount = _symbolManagers.Values.Count(m => m.PortfolioHeat > 0);
        if (openPositionsCount >= _settings.MaxConcurrentPositions)
        {
            _logger.Warning("⛔ Max concurrent positions reached: {OpenPositionsCount} >= {MaxConcurrentPositions}",
                openPositionsCount,
                _settings.MaxConcurrentPositions);
            return false;
        }

        // 4. Delegate to symbol-specific risk manager
        if (_symbolManagers.TryGetValue(symbol, out var manager))
        {
            return manager.CanOpenPosition();
        }

        // Symbol not registered - allow but warn
        _logger.Warning("⚠️ Warning: Symbol {Symbol} not registered in portfolio manager", symbol);
        return true;
    }

    public decimal GetTotalDrawdownPercent() => _equityTracker.TotalDrawdownPercent;

    public void UpdateEquity(string symbol, decimal equity)
    {
        if (_symbolManagers.TryGetValue(symbol, out var manager))
        {
            manager.UpdateEquity(equity);
        }
        _equityTracker.UpdateSymbol(symbol, equity);
    }

    public void UpdateAllEquities()
    {
        foreach (var (symbol, manager) in _symbolManagers)
        {
            _equityTracker.UpdateSymbol(symbol, manager.GetTotalEquity());
        }
    }

    public PortfolioStats GetPortfolioStats()
    {
        var openPositions = _symbolManagers.Count(kvp => kvp.Value.PortfolioHeat > 0);

        var groupRisks = _correlationGroups.ToDictionary(
            g => g.Key,
            g => g.Value.Sum(symbol =>
                _symbolManagers.TryGetValue(symbol, out var mgr) ? mgr.PortfolioHeat : 0)
        );

        var symbolEquities = _symbolManagers.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.GetTotalEquity()
        );

        return new PortfolioStats(
            _equityTracker.TotalEquity,
            _equityTracker.TotalPeakEquity,
            _equityTracker.TotalDrawdownPercent,
            openPositions,
            groupRisks,
            symbolEquities
        );
    }

    public void AddCorrelationGroup(string groupName, string[] symbols)
    {
        _correlationGroups[groupName] = symbols;
    }

    public void RemoveCorrelationGroup(string groupName)
    {
        _correlationGroups.Remove(groupName);
    }

    public Dictionary<string, string[]> GetCorrelationGroups()
    {
        return new Dictionary<string, string[]>(_correlationGroups);
    }
}
