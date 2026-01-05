using System;
using System.Collections.Generic;
using System.Linq;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Relative Strength Index
/// </summary>
public class Rsi : IIndicator<decimal>
{
    private readonly int _period;
    private readonly Queue<decimal> _prices = new();
    private decimal? _avgGain;
    private decimal? _avgLoss;

    public Rsi(int period = 14)
    {
        _period = period;
    }

    public decimal? Value { get; private set; }
    public bool IsReady => _prices.Count > _period;

    public decimal? Update(decimal price)
    {
        _prices.Enqueue(price);

        if (_prices.Count > _period + 1)
            _prices.Dequeue();

        if (_prices.Count < _period + 1)
            return null;

        var changes = _prices.Zip(_prices.Skip(1), (prev, curr) => curr - prev).ToList();
        var gains = changes.Where(c => c > 0).ToList();
        var losses = changes.Where(c => c < 0).Select(Math.Abs).ToList();

        if (_avgGain == null)
        {
            _avgGain = gains.DefaultIfEmpty(0).Average();
            _avgLoss = losses.DefaultIfEmpty(0).Average();
        }
        else
        {
            decimal currentGain = changes.Last() > 0 ? changes.Last() : 0;
            decimal currentLoss = changes.Last() < 0 ? Math.Abs(changes.Last()) : 0;
            _avgGain = (_avgGain * (_period - 1) + currentGain) / _period;
            _avgLoss = (_avgLoss * (_period - 1) + currentLoss) / _period;
        }

        if (_avgLoss == 0)
        {
            Value = 100m;
        }
        else
        {
            var rs = _avgGain / _avgLoss;
            Value = 100m - (100m / (1m + rs));
        }

        return Value;
    }

    public void Reset()
    {
        _prices.Clear();
        _avgGain = null;
        _avgLoss = null;
        Value = null;
    }
}
