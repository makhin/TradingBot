using System;
using System.Collections.Generic;
using System.Linq;
using SimpleBot.Models;

namespace SimpleBot.Services;

public class RsiStrategy : IStrategy
{
    private readonly Queue<decimal> _prices = new();
    private readonly int _period;
    private readonly decimal _overbought;
    private readonly decimal _oversold;
    private SignalType _lastSignal = SignalType.None;

    public RsiStrategy(int period = 14, decimal overbought = 70m, decimal oversold = 30m)
    {
        _period = period;
        _overbought = overbought;
        _oversold = oversold;
    }

    public TradeSignal? AnalyzePrice(MarketData data, decimal minTradeAmount = 10m)
    {
        _prices.Enqueue(data.Price);

        while (_prices.Count > _period + 1)
            _prices.Dequeue();

        if (_prices.Count < _period + 1)
            return null;

        var rsi = CalculateRSI();
        
        Console.WriteLine($"📊 {data.Symbol}: Price={data.Price:F2}, RSI={rsi:F2}");

        // Oversold (< 30) = Buy signal
        if (rsi < _oversold && _lastSignal != SignalType.Buy)
        {
            _lastSignal = SignalType.Buy;
            Console.WriteLine($"🔵 RSI Oversold detected!");
            return new TradeSignal(data.Symbol, SignalType.Buy, data.Price, minTradeAmount);
        }

        // Overbought (> 70) = Sell signal
        if (rsi > _overbought && _lastSignal != SignalType.Sell)
        {
            _lastSignal = SignalType.Sell;
            Console.WriteLine($"🔴 RSI Overbought detected!");
            // Round quantity to 5 decimal places (Binance LOT_SIZE requirement for BTC)
            var quantity = Math.Round(minTradeAmount / data.Price, 5);
            return new TradeSignal(data.Symbol, SignalType.Sell, data.Price, quantity);
        }

        return null;
    }

    private decimal CalculateRSI()
    {
        var changes = _prices.Zip(_prices.Skip(1), (prev, curr) => curr - prev).ToList();
        
        var gains = changes.Where(c => c > 0).DefaultIfEmpty(0).Average();
        var losses = changes.Where(c => c < 0).Select(Math.Abs).DefaultIfEmpty(0).Average();

        if (losses == 0)
            return 100m;

        var rs = gains / losses;
        return 100m - (100m / (1m + rs));
    }
}