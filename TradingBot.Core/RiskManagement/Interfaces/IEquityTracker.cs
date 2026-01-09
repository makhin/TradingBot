namespace TradingBot.Core.RiskManagement;

public interface IEquityTracker
{
    decimal CurrentEquity { get; }
    decimal PeakEquity { get; }
    decimal DayStartEquity { get; }
    decimal DrawdownPercent { get; }
    decimal DailyDrawdownPercent { get; }

    void Update(decimal newEquity);
    void Add(decimal amount);
    void ResetDailyTracking();
    bool IsDailyDrawdownExceeded(decimal threshold);
    void RestoreState(decimal currentEquity, decimal peakEquity, decimal dayStartEquity);
}
