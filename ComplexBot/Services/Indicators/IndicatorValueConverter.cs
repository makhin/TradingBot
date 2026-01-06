using System;

namespace ComplexBot.Services.Indicators;

internal static class IndicatorValueConverter
{
    public static decimal? ToDecimal<T>(T? value) where T : struct, IConvertible
        => value.HasValue ? Convert.ToDecimal(value.Value) : null;
}
