using ComplexBot.Models;

namespace ComplexBot.Services.RiskManagement;

public class RiskManager
{
    private readonly RiskSettings _settings;
    private decimal _peakEquity;
    private decimal _currentEquity;
    private readonly List<OpenPosition> _openPositions = new();
    private decimal _dayStartEquity;
    private DateTime _currentTradingDay;

    public RiskManager(RiskSettings settings, decimal initialCapital)
    {
        _settings = settings;
        _peakEquity = initialCapital;
        _currentEquity = initialCapital;
        _dayStartEquity = initialCapital;
        _currentTradingDay = DateTime.UtcNow.Date;
    }

    public decimal CurrentDrawdown => _peakEquity > 0 
        ? (_peakEquity - _currentEquity) / _peakEquity * 100 
        : 0;

    public decimal PortfolioHeat => _currentEquity <= 0
        ? 100
        : _openPositions.Sum(p => p.RiskAmount) / _currentEquity * 100;

    public void ResetDailyTracking()
    {
        var today = DateTime.UtcNow.Date;
        if (_currentTradingDay != today)
        {
            _dayStartEquity = _currentEquity;
            _currentTradingDay = today;
        }
    }

    public decimal GetDailyDrawdownPercent()
    {
        ResetDailyTracking();
        if (_dayStartEquity <= 0) return 0;
        return (_dayStartEquity - _currentEquity) / _dayStartEquity * 100;
    }

    public bool IsDailyLimitExceeded()
    {
        return GetDailyDrawdownPercent() >= _settings.MaxDailyDrawdownPercent;
    }

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
        if (_currentEquity <= 0)
            return new PositionSizeResult(0, 0, 0);

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
        decimal total = 0;
        foreach (var pos in _openPositions)
        {
            var pnl = pos.Direction == SignalType.Buy
                ? (pos.CurrentPrice - pos.EntryPrice) * pos.RemainingQuantity
                : (pos.EntryPrice - pos.CurrentPrice) * pos.RemainingQuantity;
            total += pnl;
        }
        return total;
    }

    public decimal GetTotalEquity()
    {
        return _currentEquity + GetUnrealizedPnL();
    }

    public decimal GetTotalDrawdownPercent()
    {
        var totalEquity = GetTotalEquity();
        if (_peakEquity <= 0) return 0;
        return (_peakEquity - totalEquity) / _peakEquity * 100;
    }

    public bool CanOpenPosition()
    {
        if (_currentEquity <= 0)
            return false;

        // Check minimum equity requirement
        if (_currentEquity < _settings.MinimumEquityUsd)
        {
            Console.WriteLine($"⛔ Equity below minimum: ${_currentEquity:F2} < ${_settings.MinimumEquityUsd:F2}");
            return false;
        }

        // Check daily loss limit (with unrealized P&L)
        if (IsDailyLimitExceeded())
        {
            Console.WriteLine($"⛔ Daily loss limit exceeded: {GetDailyDrawdownPercent():F2}%");
            return false;
        }

        // Check total drawdown (including unrealized P&L)
        var totalDrawdown = GetTotalDrawdownPercent();
        if (totalDrawdown >= _settings.MaxDrawdownPercent)
        {
            Console.WriteLine($"⛔ Max drawdown exceeded (including unrealized): {totalDrawdown:F2}%");
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

public record OpenPosition(
    string Symbol,
    SignalType Direction,
    decimal Quantity,
    decimal RemainingQuantity,
    decimal RiskAmount,
    decimal EntryPrice,
    decimal StopLoss,
    bool BreakevenMoved,
    decimal CurrentPrice
);

public record RiskSettings
{
    public decimal RiskPerTradePercent { get; init; } = 1.5m;  // 1.5% per trade
    public decimal MaxPortfolioHeatPercent { get; init; } = 15m;  // 15% max heat
    public decimal MaxDrawdownPercent { get; init; } = 20m;  // 20% circuit breaker
    public decimal MaxDailyDrawdownPercent { get; init; } = 3m;  // 3% daily loss limit
    public decimal AtrStopMultiplier { get; init; } = 2.5m;  // 2.5x ATR for stops
    public decimal TakeProfitMultiplier { get; init; } = 1.5m;  // 1.5:1 reward:risk
    public decimal MinimumEquityUsd { get; init; } = 100m;  // Minimum $100 to trade
}
