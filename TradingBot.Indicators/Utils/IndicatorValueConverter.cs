using System;

namespace TradingBot.Indicators.Utils;

internal static class IndicatorValueConverter
{
    public static decimal? ToDecimal<T>(T? value) where T : struct, IConvertible
        => value.HasValue ? Convert.ToDecimal(value.Value) : null;
}
