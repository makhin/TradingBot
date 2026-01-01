using ComplexBot.Models;

namespace ComplexBot.Services.RiskManagement;

public class RiskManager
{
    private readonly RiskSettings _settings;
    private decimal _peakEquity;
    private decimal _currentEquity;
    private readonly List<OpenPosition> _openPositions = new();

    public RiskManager(RiskSettings settings, decimal initialCapital)
    {
        _settings = settings;
        _peakEquity = initialCapital;
        _currentEquity = initialCapital;
    }

    public decimal CurrentDrawdown => _peakEquity > 0 
        ? (_peakEquity - _currentEquity) / _peakEquity * 100 
        : 0;

    public decimal PortfolioHeat => _openPositions.Sum(p => p.RiskAmount) / _currentEquity * 100;

    public void UpdateEquity(decimal equity)
    {
        _currentEquity = equity;
        if (equity > _peakEquity)
            _peakEquity = equity;
    }

    public PositionSizeResult CalculatePositionSize(
        decimal entryPrice,
        decimal stopLossPrice,
        decimal? atr = null)
    {
        // Calculate stop distance
        decimal stopDistance = Math.Abs(entryPrice - stopLossPrice);
        
        // If ATR provided, use it for minimum stop distance
        if (atr.HasValue)
            stopDistance = Math.Max(stopDistance, atr.Value * _settings.AtrStopMultiplier);

        // Apply drawdown adjustment
        decimal adjustedRiskPercent = GetDrawdownAdjustedRisk();
        
        // Calculate risk amount
        decimal riskAmount = _currentEquity * adjustedRiskPercent / 100;
        
        // Check portfolio heat limit
        decimal currentHeat = PortfolioHeat;
        if (currentHeat + adjustedRiskPercent > _settings.MaxPortfolioHeatPercent)
        {
            decimal availableRisk = Math.Max(0, _settings.MaxPortfolioHeatPercent - currentHeat);
            riskAmount = _currentEquity * availableRisk / 100;
        }

        // Calculate quantity
        decimal quantity = stopDistance > 0 ? riskAmount / stopDistance : 0;

        return new PositionSizeResult(quantity, riskAmount, stopDistance);
    }

    public decimal GetDrawdownAdjustedRisk()
    {
        decimal baseRisk = _settings.RiskPerTradePercent;
        decimal drawdown = CurrentDrawdown;

        // Jerry Parker's rule: reduce position size during drawdowns
        return drawdown switch
        {
            >= 20 => baseRisk * 0.25m,  // 75% reduction
            >= 15 => baseRisk * 0.50m,  // 50% reduction
            >= 10 => baseRisk * 0.75m,  // 25% reduction
            >= 5 => baseRisk * 0.90m,   // 10% reduction
            _ => baseRisk
        };
    }

    public bool CanOpenPosition()
    {
        // Check drawdown circuit breaker
        if (CurrentDrawdown >= _settings.MaxDrawdownPercent)
            return false;

        // Check portfolio heat
        if (PortfolioHeat >= _settings.MaxPortfolioHeatPercent)
            return false;

        return true;
    }

    public void AddPosition(string symbol, decimal quantity, decimal riskAmount, decimal entryPrice, decimal stopLoss)
    {
        _openPositions.Add(new OpenPosition(symbol, quantity, riskAmount, entryPrice, stopLoss));
    }

    public void RemovePosition(string symbol)
    {
        _openPositions.RemoveAll(p => p.Symbol == symbol);
    }

    public void ClearPositions() => _openPositions.Clear();
}

public record OpenPosition(
    string Symbol,
    decimal Quantity,
    decimal RiskAmount,
    decimal EntryPrice,
    decimal StopLoss
);

public record RiskSettings
{
    public decimal RiskPerTradePercent { get; init; } = 1.5m;  // 1.5% per trade
    public decimal MaxPortfolioHeatPercent { get; init; } = 15m;  // 15% max heat
    public decimal MaxDrawdownPercent { get; init; } = 20m;  // 20% circuit breaker
    public decimal AtrStopMultiplier { get; init; } = 2.5m;  // 2.5x ATR for stops
    public decimal TakeProfitMultiplier { get; init; } = 1.5m;  // 1.5:1 reward:risk
}
