using System;
using System.Collections.Generic;
using System.Linq;
using SimpleBot.Models;

namespace SimpleBot.Services;

public class SimpleMaStrategy : IStrategy
{
    private readonly Queue<decimal> _prices = new();
    private readonly int _shortPeriod;
    private readonly int _longPeriod;
    private SignalType _lastSignal = SignalType.None;

    public SimpleMaStrategy(int shortPeriod = 5, int longPeriod = 20)
    {
        _shortPeriod = shortPeriod;
        _longPeriod = longPeriod;
    }

    public TradeSignal? AnalyzePrice(MarketData data, decimal minTradeAmount = 10m)
    {
        _prices.Enqueue(data.Price);

        // Keep only the prices we need
        while (_prices.Count > _longPeriod)
            _prices.Dequeue();

        if (_prices.Count < _longPeriod)
            return null; // Not enough data yet

        var shortMa = CalculateMA(_shortPeriod);
        var longMa = CalculateMA(_longPeriod);

        Console.WriteLine($"📊 {data.Symbol}: Price={data.Price:F2}, Short MA={shortMa:F2}, Long MA={longMa:F2}");

        // Golden cross: short MA crosses above long MA = Buy signal
        if (shortMa > longMa && _lastSignal != SignalType.Buy)
        {
            _lastSignal = SignalType.Buy;
            return new TradeSignal(data.Symbol, SignalType.Buy, data.Price, minTradeAmount);
        }

        // Death cross: short MA crosses below long MA = Sell signal
        if (shortMa < longMa && _lastSignal != SignalType.Sell)
        {
            _lastSignal = SignalType.Sell;
            // Round quantity to 5 decimal places (Binance LOT_SIZE requirement for BTC)
            var quantity = Math.Round(minTradeAmount / data.Price, 5);
            return new TradeSignal(data.Symbol, SignalType.Sell, data.Price, quantity);
        }

        return null;
    }

    private decimal CalculateMA(int period)
    {
        return _prices.TakeLast(period).Average();
    }
}