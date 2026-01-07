using System;
using System.Collections.Generic;
using ComplexBot.Models;
using Skender.Stock.Indicators;

namespace ComplexBot.Services.Indicators;

public sealed class QuoteSeries
{
    private readonly List<Quote> _quotes = new();
    private DateTime _nextTimestamp = DateTime.UnixEpoch;

    public IReadOnlyList<Quote> Quotes => _quotes;

    public void AddPrice(decimal price)
    {
        _quotes.Add(new Quote
        {
            Date = _nextTimestamp,
            Open = price,
            High = price,
            Low = price,
            Close = price,
            Volume = 0
        });

        _nextTimestamp = _nextTimestamp.AddMinutes(1);
    }

    public void AddCandle(Candle candle)
    {
        _quotes.Add(candle.ToQuote());
    }

    public void Reset()
    {
        _quotes.Clear();
        _nextTimestamp = DateTime.UnixEpoch;
    }
}
