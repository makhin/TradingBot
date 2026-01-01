using System;
using SimpleBot.Models;

namespace SimpleBot.Services;

public static class StrategyFactory
{
    public static IStrategy CreateStrategy(StrategySettings settings)
    {
        return settings.Type.ToUpperInvariant() switch
        {
            "MA" => new SimpleMaStrategy(settings.ShortPeriod, settings.LongPeriod),
            "RSI" => new RsiStrategy(settings.RsiPeriod, settings.RsiOverbought, settings.RsiOversold),
            "BOLLINGERBANDS" or "BB" => new BollingerBandsStrategy(settings.BollingerPeriod, settings.BollingerStdDev),
            "COMPOSITE" => new CompositeStrategy(
                settings.ShortPeriod, settings.LongPeriod,
                settings.RsiPeriod, settings.RsiOverbought, settings.RsiOversold),
            _ => throw new ArgumentException($"Unknown strategy type: {settings.Type}. Valid types are: MA, RSI, BollingerBands, Composite")
        };
    }

    public static string GetStrategyName(string type)
    {
        return type.ToUpperInvariant() switch
        {
            "MA" => "Moving Average",
            "RSI" => "RSI (Relative Strength Index)",
            "BOLLINGERBANDS" or "BB" => "Bollinger Bands",
            "COMPOSITE" => "Composite (MA + RSI)",
            _ => type
        };
    }
}
