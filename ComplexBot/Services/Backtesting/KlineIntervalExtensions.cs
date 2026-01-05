using Binance.Net.Enums;

namespace ComplexBot.Services.Backtesting;

public static class KlineIntervalExtensions
{
    public static KlineInterval Parse(string interval) => interval.ToLower() switch
    {
        "1m" => KlineInterval.OneMinute,
        "5m" => KlineInterval.FiveMinutes,
        "15m" => KlineInterval.FifteenMinutes,
        "30m" => KlineInterval.ThirtyMinutes,
        "1h" => KlineInterval.OneHour,
        "4h" => KlineInterval.FourHour,
        "1d" => KlineInterval.OneDay,
        "1w" => KlineInterval.OneWeek,
        _ => KlineInterval.OneDay
    };
}
