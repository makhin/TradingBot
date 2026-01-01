using SimpleBot.Models;

namespace SimpleBot.Services;

public class CompositeStrategy : IStrategy
{
    private readonly SimpleMaStrategy _ma;
    private readonly RsiStrategy _rsi;
    private SignalType _lastSignal = SignalType.None;

    public CompositeStrategy(int maShortPeriod = 5, int maLongPeriod = 20,
                             int rsiPeriod = 14, decimal rsiOverbought = 70m, decimal rsiOversold = 30m)
    {
        _ma = new SimpleMaStrategy(maShortPeriod, maLongPeriod);
        _rsi = new RsiStrategy(rsiPeriod, rsiOverbought, rsiOversold);
    }

    public TradeSignal? AnalyzePrice(MarketData data, decimal minTradeAmount = 10m)
    {
        var maSignal = _ma.AnalyzePrice(data, minTradeAmount);
        var rsiSignal = _rsi.AnalyzePrice(data, minTradeAmount);

        // Покупаем только если обе стратегии согласны
        if (maSignal?.Type == SignalType.Buy
            && rsiSignal?.Type == SignalType.Buy
            && _lastSignal != SignalType.Buy)
        {
            _lastSignal = SignalType.Buy;
            return maSignal;
        }

        // Продаем только если обе стратегии согласны
        if (maSignal?.Type == SignalType.Sell
            && rsiSignal?.Type == SignalType.Sell
            && _lastSignal != SignalType.Sell)
        {
            _lastSignal = SignalType.Sell;
            return maSignal;
        }

        return null;
    }
}