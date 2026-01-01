using SimpleBot.Models;

namespace SimpleBot.Services;

public interface IStrategy
{
    TradeSignal? AnalyzePrice(MarketData data, decimal minTradeAmount);
}
