namespace TradingBot.Core.RiskManagement;

/// <summary>
/// Snapshot of equity tracker state for persistence and restoration
/// </summary>
public record EquitySnapshot
{
    public decimal CurrentEquity { get; init; }
    public decimal PeakEquity { get; init; }
    public decimal DayStartEquity { get; init; }
}
