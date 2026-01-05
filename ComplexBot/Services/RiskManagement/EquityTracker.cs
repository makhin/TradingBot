using System;

namespace ComplexBot.Services.RiskManagement;

/// <summary>
/// Tracks equity, peak equity, and calculates drawdowns
/// Reusable component for both single-symbol and portfolio risk management
/// </summary>
public class EquityTracker
{
    private decimal _currentEquity;
    private decimal _peakEquity;
    private decimal _dayStartEquity;
    private DateTime _currentTradingDay;

    public EquityTracker(decimal initialCapital)
    {
        _currentEquity = initialCapital;
        _peakEquity = initialCapital;
        _dayStartEquity = initialCapital;
        _currentTradingDay = DateTime.UtcNow.Date;
    }

    /// <summary>
    /// Current equity value
    /// </summary>
    public decimal CurrentEquity => _currentEquity;

    /// <summary>
    /// Historical peak equity (high water mark)
    /// </summary>
    public decimal PeakEquity => _peakEquity;

    /// <summary>
    /// Equity at the start of current trading day
    /// </summary>
    public decimal DayStartEquity => _dayStartEquity;

    /// <summary>
    /// Current drawdown from peak as percentage
    /// </summary>
    public decimal DrawdownPercent => _peakEquity > 0
        ? (_peakEquity - _currentEquity) / _peakEquity * 100
        : 0;

    /// <summary>
    /// Current drawdown from peak as absolute value
    /// </summary>
    public decimal DrawdownAbsolute => _peakEquity - _currentEquity;

    /// <summary>
    /// Daily drawdown from day start as percentage
    /// </summary>
    public decimal DailyDrawdownPercent
    {
        get
        {
            CheckDayRollover();
            return _dayStartEquity > 0
                ? (_dayStartEquity - _currentEquity) / _dayStartEquity * 100
                : 0;
        }
    }

    /// <summary>
    /// Updates equity and peak equity if new high
    /// </summary>
    public void Update(decimal newEquity)
    {
        CheckDayRollover();
        _currentEquity = newEquity;
        if (newEquity > _peakEquity)
            _peakEquity = newEquity;
    }

    /// <summary>
    /// Adds to current equity (e.g., realized PnL)
    /// </summary>
    public void Add(decimal amount)
    {
        Update(_currentEquity + amount);
    }

    /// <summary>
    /// Checks if drawdown exceeds threshold
    /// </summary>
    public bool IsDrawdownExceeded(decimal maxDrawdownPercent)
    {
        return DrawdownPercent >= maxDrawdownPercent;
    }

    /// <summary>
    /// Checks if daily drawdown exceeds threshold
    /// </summary>
    public bool IsDailyDrawdownExceeded(decimal maxDailyDrawdownPercent)
    {
        return DailyDrawdownPercent >= maxDailyDrawdownPercent;
    }

    /// <summary>
    /// Manually resets peak equity (use with caution)
    /// </summary>
    public void ResetPeak()
    {
        _peakEquity = _currentEquity;
    }

    /// <summary>
    /// Resets daily tracking to current equity
    /// </summary>
    public void ResetDailyTracking()
    {
        _dayStartEquity = _currentEquity;
        _currentTradingDay = DateTime.UtcNow.Date;
    }

    private void CheckDayRollover()
    {
        var today = DateTime.UtcNow.Date;
        if (_currentTradingDay != today)
        {
            _dayStartEquity = _currentEquity;
            _currentTradingDay = today;
        }
    }
}
