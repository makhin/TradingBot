namespace ComplexBot.Models;

public static class IndicatorValueKeyExtensions
{
    public static string ToDisplayName(this IndicatorValueKey key)
        => key switch
        {
            IndicatorValueKey.Adx => "ADX",
            IndicatorValueKey.PlusDi => "+DI",
            IndicatorValueKey.MinusDi => "-DI",
            IndicatorValueKey.FastEma => "FastEMA",
            IndicatorValueKey.SlowEma => "SlowEMA",
            IndicatorValueKey.Atr => "ATR",
            IndicatorValueKey.MacdLine => "MACD",
            IndicatorValueKey.MacdSignal => "Signal",
            IndicatorValueKey.MacdHistogram => "MACD_Hist",
            IndicatorValueKey.VolumeRatio => "VolumeRatio",
            IndicatorValueKey.ObvSlope => "OBV_Slope",
            IndicatorValueKey.BollingerMiddle => "Middle",
            IndicatorValueKey.BollingerUpper => "Upper",
            IndicatorValueKey.BollingerLower => "Lower",
            _ => key.ToString()
        };
}
