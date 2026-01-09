using System.Collections.Generic;

namespace TradingBot.Core.RiskManagement;

public record PortfolioStats(
    decimal TotalEquity,
    decimal PeakEquity,
    decimal DrawdownPercent,
    int OpenPositions,
    Dictionary<string, decimal> GroupRisks,
    Dictionary<string, decimal> SymbolEquities
);
