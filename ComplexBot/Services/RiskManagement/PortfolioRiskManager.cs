using ComplexBot.Models;

namespace ComplexBot.Services.RiskManagement;

public class PortfolioRiskManager
{
    private readonly Dictionary<string, RiskManager> _symbolManagers = new();
    private readonly PortfolioRiskSettings _settings;
    private readonly Dictionary<string, string[]> _correlationGroups;
    private readonly AggregatedEquityTracker _equityTracker = new();

    public PortfolioRiskManager(PortfolioRiskSettings settings)
    {
        _settings = settings;

        // Initialize correlation groups
        _correlationGroups = new Dictionary<string, string[]>
        {
            ["BTC_CORRELATED"] = ["BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT"],
            ["ALTCOINS_L1"] = ["ADAUSDT", "DOTUSDT", "AVAXUSDT", "MATICUSDT"],
            ["ALTCOINS_DEFI"] = ["UNIUSDT", "AAVEUSDT", "LINKUSDT", "SUSHIUSDT"],
            ["MEMECOINS"] = ["DOGEUSDT", "SHIBUSDT", "PEPEUSDT"]
        };
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
            Console.WriteLine($"⛔ Portfolio drawdown exceeded: {totalDrawdown:F2}% >= {_settings.MaxTotalDrawdownPercent:F2}%");
            return false;
        }

        // 2. Check correlated risk
        var correlatedRisk = GetCorrelatedRisk(symbol);
        if (correlatedRisk >= _settings.MaxCorrelatedRiskPercent)
        {
            var groupName = _correlationGroups
                .FirstOrDefault(g => g.Value.Contains(symbol)).Key ?? "Unknown";
            Console.WriteLine($"⛔ Correlated risk too high for {symbol} (group: {groupName}): {correlatedRisk:F2}% >= {_settings.MaxCorrelatedRiskPercent:F2}%");
            return false;
        }

        // 3. Check max concurrent positions
        var openPositionsCount = _symbolManagers.Values.Count(m => m.PortfolioHeat > 0);
        if (openPositionsCount >= _settings.MaxConcurrentPositions)
        {
            Console.WriteLine($"⛔ Max concurrent positions reached: {openPositionsCount} >= {_settings.MaxConcurrentPositions}");
            return false;
        }

        // 4. Delegate to symbol-specific risk manager
        if (_symbolManagers.TryGetValue(symbol, out var manager))
        {
            return manager.CanOpenPosition();
        }

        // Symbol not registered - allow but warn
        Console.WriteLine($"⚠️ Warning: Symbol {symbol} not registered in portfolio manager");
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

public record PortfolioRiskSettings
{
    public decimal MaxTotalDrawdownPercent { get; init; } = 25m;  // 25% max portfolio drawdown
    public decimal MaxCorrelatedRiskPercent { get; init; } = 10m;  // 10% max risk on correlated assets
    public int MaxConcurrentPositions { get; init; } = 5;  // Max 5 positions at once
}

public record PortfolioStats(
    decimal TotalEquity,
    decimal PeakEquity,
    decimal DrawdownPercent,
    int OpenPositions,
    Dictionary<string, decimal> GroupRisks,
    Dictionary<string, decimal> SymbolEquities
);
