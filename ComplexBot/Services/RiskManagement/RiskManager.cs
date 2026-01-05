using ComplexBot.Models;

namespace ComplexBot.Services.RiskManagement;

public class RiskManager
{
    private readonly RiskSettings _settings;
    private readonly EquityTracker _equityTracker;
    private readonly List<OpenPosition> _openPositions = new();

    public RiskManager(RiskSettings settings, decimal initialCapital)
    {
        _settings = settings;
        _equityTracker = new EquityTracker(initialCapital);
    }

    public decimal CurrentDrawdown => _equityTracker.DrawdownPercent;

    public decimal PortfolioHeat => _equityTracker.CurrentEquity <= 0
        ? 100
        : _openPositions.Sum(p => p.RiskAmount) / _equityTracker.CurrentEquity * 100;

    public void ResetDailyTracking() => _equityTracker.ResetDailyTracking();

    public decimal GetDailyDrawdownPercent() => _equityTracker.DailyDrawdownPercent;

    public bool IsDailyLimitExceeded() =>
        _equityTracker.IsDailyDrawdownExceeded(_settings.MaxDailyDrawdownPercent);

    public void UpdateEquity(decimal equity) => _equityTracker.Update(equity);

    public PositionSizeResult CalculatePositionSize(
        decimal entryPrice,
        decimal stopLossPrice,
        decimal? atr = null)
    {
        var currentEquity = _equityTracker.CurrentEquity;
        if (currentEquity <= 0)
            return new PositionSizeResult(0, 0, 0);

        // Calculate stop distance
        decimal stopDistance = Math.Abs(entryPrice - stopLossPrice);

        // If ATR provided, use it for minimum stop distance
        if (atr.HasValue)
            stopDistance = Math.Max(stopDistance, atr.Value * _settings.AtrStopMultiplier);

        // Apply drawdown adjustment
        decimal adjustedRiskPercent = GetDrawdownAdjustedRisk();

        // Calculate risk amount
        decimal riskAmount = currentEquity * adjustedRiskPercent / 100;

        // Check portfolio heat limit
        decimal currentHeat = PortfolioHeat;
        if (currentHeat + adjustedRiskPercent > _settings.MaxPortfolioHeatPercent)
        {
            decimal availableRisk = Math.Max(0, _settings.MaxPortfolioHeatPercent - currentHeat);
            riskAmount = currentEquity * availableRisk / 100;
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

    public void UpdatePositionPrice(string symbol, decimal currentPrice)
    {
        var position = _openPositions.FirstOrDefault(p => p.Symbol == symbol);
        if (position != null)
        {
            _openPositions.Remove(position);
            _openPositions.Add(position with { CurrentPrice = currentPrice });
        }
    }

    public decimal GetUnrealizedPnL()
    {
        return _openPositions.Sum(pos =>
            pos.Direction == SignalType.Buy
                ? (pos.CurrentPrice - pos.EntryPrice) * pos.RemainingQuantity
                : (pos.EntryPrice - pos.CurrentPrice) * pos.RemainingQuantity);
    }

    public decimal GetTotalEquity() => _equityTracker.CurrentEquity + GetUnrealizedPnL();

    public decimal GetTotalDrawdownPercent()
    {
        var totalEquity = GetTotalEquity();
        var peakEquity = _equityTracker.PeakEquity;
        if (peakEquity <= 0) return 0;
        return (peakEquity - totalEquity) / peakEquity * 100;
    }

    public bool CanOpenPosition()
    {
        var currentEquity = _equityTracker.CurrentEquity;

        if (currentEquity <= 0)
            return false;

        // Check minimum equity requirement
        if (currentEquity < _settings.MinimumEquityUsd)
        {
            Console.WriteLine($"Equity below minimum: ${currentEquity:F2} < ${_settings.MinimumEquityUsd:F2}");
            return false;
        }

        // Check daily loss limit (with unrealized P&L)
        if (IsDailyLimitExceeded())
        {
            Console.WriteLine($"Daily loss limit exceeded: {GetDailyDrawdownPercent():F2}%");
            return false;
        }

        // Check total drawdown (including unrealized P&L)
        var totalDrawdown = GetTotalDrawdownPercent();
        if (totalDrawdown >= _settings.MaxDrawdownPercent)
        {
            Console.WriteLine($"Max drawdown exceeded (including unrealized): {totalDrawdown:F2}%");
            return false;
        }

        // Check portfolio heat
        if (PortfolioHeat >= _settings.MaxPortfolioHeatPercent)
            return false;

        return true;
    }

    public void AddPosition(string symbol, SignalType direction, decimal quantity, decimal riskAmount, decimal entryPrice, decimal stopLoss, decimal currentPrice)
    {
        _openPositions.Add(new OpenPosition(
            symbol,
            direction,
            quantity,
            quantity,
            riskAmount,
            entryPrice,
            stopLoss,
            false,
            currentPrice));
    }

    public void RemovePosition(string symbol)
    {
        _openPositions.RemoveAll(p => p.Symbol == symbol);
    }

    public void UpdatePositionAfterPartialExit(
        string symbol,
        decimal remainingQuantity,
        decimal stopLoss,
        bool breakevenMoved,
        decimal currentPrice)
    {
        var position = _openPositions.FirstOrDefault(p => p.Symbol == symbol);
        if (position == null)
            return;

        decimal riskAmount = Math.Abs(position.EntryPrice - stopLoss) * remainingQuantity;
        _openPositions.Remove(position);
        _openPositions.Add(position with
        {
            RemainingQuantity = remainingQuantity,
            RiskAmount = riskAmount,
            StopLoss = stopLoss,
            BreakevenMoved = breakevenMoved,
            CurrentPrice = currentPrice
        });
    }

    public void ClearPositions() => _openPositions.Clear();
}
