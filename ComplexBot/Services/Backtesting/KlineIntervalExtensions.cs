using ComplexBot.Models;

namespace ComplexBot.Services.Backtesting;

public static class KlineIntervalExtensions
{
    public static Binance.Net.Enums.KlineInterval ToBinanceInterval(this KlineInterval interval) => interval switch
    {
        KlineInterval.OneMinute => Binance.Net.Enums.KlineInterval.OneMinute,
        KlineInterval.FiveMinutes => Binance.Net.Enums.KlineInterval.FiveMinutes,
        KlineInterval.FifteenMinutes => Binance.Net.Enums.KlineInterval.FifteenMinutes,
        KlineInterval.ThirtyMinutes => Binance.Net.Enums.KlineInterval.ThirtyMinutes,
        KlineInterval.OneHour => Binance.Net.Enums.KlineInterval.OneHour,
        KlineInterval.FourHour => Binance.Net.Enums.KlineInterval.FourHour,
        KlineInterval.OneDay => Binance.Net.Enums.KlineInterval.OneDay,
        KlineInterval.OneWeek => Binance.Net.Enums.KlineInterval.OneWeek,
        _ => Binance.Net.Enums.KlineInterval.OneDay
    };
}
