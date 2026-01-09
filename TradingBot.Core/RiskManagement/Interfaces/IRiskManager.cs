using TradingBot.Core.Models;

namespace TradingBot.Core.RiskManagement;

public interface IRiskManager
{
    decimal CurrentDrawdown { get; }
    decimal PortfolioHeat { get; }

    PositionSizeResult CalculatePositionSize(
        decimal entryPrice,
        decimal stopLossPrice,
        decimal? atr = null);

    decimal GetDrawdownAdjustedRisk();
    bool CanOpenPosition();

    void UpdateEquity(decimal equity);
    void AddPosition(string symbol, SignalType direction, decimal quantity,
        decimal riskAmount, decimal entryPrice, decimal stopLoss, decimal currentPrice);
    void RemovePosition(string symbol);
    void UpdatePositionAfterPartialExit(string symbol, decimal remainingQuantity,
        decimal stopLoss, bool breakevenMoved, decimal currentPrice);
    void UpdatePositionPrice(string symbol, decimal currentPrice);

    decimal GetUnrealizedPnL();
    decimal GetTotalEquity();
    decimal GetTotalDrawdownPercent();

    void RestoreEquityState(decimal currentEquity, decimal peakEquity, decimal dayStartEquity);
    EquitySnapshot GetEquitySnapshot();
    void ResetDailyTracking();
    decimal GetDailyDrawdownPercent();
    bool IsDailyLimitExceeded();
    decimal GetTotalRiskAmount();
    void ClearPositions();
}
