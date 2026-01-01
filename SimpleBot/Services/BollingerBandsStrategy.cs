using System;
using System.Collections.Generic;
using System.Linq;
using SimpleBot.Models;

namespace SimpleBot.Services;

public class BollingerBandsStrategy : IStrategy
{
    private readonly Queue<decimal> _prices = new();
    private readonly int _period;
    private readonly decimal _stdDevMultiplier;
    private SignalType _lastSignal = SignalType.None;

    public BollingerBandsStrategy(int period = 20, decimal stdDevMultiplier = 2m)
    {
        _period = period;
        _stdDevMultiplier = stdDevMultiplier;
    }

    public TradeSignal? AnalyzePrice(MarketData data, decimal minTradeAmount = 10m)
    {
        _prices.Enqueue(data.Price);

        while (_prices.Count > _period)
            _prices.Dequeue();

        if (_prices.Count < _period)
            return null;

        var (middle, upper, lower) = CalculateBands();

        Console.WriteLine($"📊 {data.Symbol}: Price={data.Price:F2}, Upper={upper:F2}, Middle={middle:F2}, Lower={lower:F2}");

        // Price touches lower band = Buy signal
        if (data.Price <= lower && _lastSignal != SignalType.Buy)
        {
            _lastSignal = SignalType.Buy;
            Console.WriteLine($"🔵 Price at lower Bollinger Band!");
            return new TradeSignal(data.Symbol, SignalType.Buy, data.Price, minTradeAmount);
        }

        // Price touches upper band = Sell signal
        if (data.Price >= upper && _lastSignal != SignalType.Sell)
        {
            _lastSignal = SignalType.Sell;
            Console.WriteLine($"🔴 Price at upper Bollinger Band!");
            // Round quantity to 5 decimal places (Binance LOT_SIZE requirement for BTC)
            var quantity = Math.Round(minTradeAmount / data.Price, 5);
            return new TradeSignal(data.Symbol, SignalType.Sell, data.Price, quantity);
        }

        return null;
    }

    private (decimal middle, decimal upper, decimal lower) CalculateBands()
    {
        var middle = _prices.Average();
        var variance = _prices.Average(p => (p - middle) * (p - middle));
        var stdDev = (decimal)Math.Sqrt((double)variance);

        var upper = middle + (_stdDevMultiplier * stdDev);
        var lower = middle - (_stdDevMultiplier * stdDev);

        return (middle, upper, lower);
    }
}