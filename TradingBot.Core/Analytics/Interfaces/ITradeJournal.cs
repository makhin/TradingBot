using System.Collections.Generic;
using TradingBot.Core.Models;

namespace TradingBot.Core.Analytics;

public interface ITradeJournal
{
    int OpenTrade(TradeJournalEntry entry);
    void CloseTrade(int tradeId, TradeJournalEntry updates);
    void ExportToCsv(string? filename = null);
    TradeJournalStats GetStats();
    IReadOnlyList<TradeJournalEntry> GetAllTrades();
}
