using TradingBot.Core.Models;
using Bitget.Net.Enums;
using Bitget.Net.Enums.V2;
using CoreKlineInterval = TradingBot.Core.Models.KlineInterval;
using CorePositionSide = TradingBot.Core.Models.PositionSide;
using CoreMarginType = TradingBot.Core.Models.MarginType;
using BitgetMarginMode = Bitget.Net.Enums.V2.MarginMode;

namespace TradingBot.Bitget.Common;

/// <summary>
/// Helper methods and enum mappings for Bitget using JK.Bitget.Net v3.4.0
/// </summary>
public static class BitgetHelpers
{
    /// <summary>
    /// Maps Core KlineInterval to Bitget FuturesKlineInterval enum (for REST API)
    /// </summary>
    public static BitgetFuturesKlineInterval MapKlineInterval(CoreKlineInterval interval)
    {
        return interval switch
        {
            CoreKlineInterval.OneMinute => BitgetFuturesKlineInterval.OneMinute,
            CoreKlineInterval.FiveMinutes => BitgetFuturesKlineInterval.FiveMinutes,
            CoreKlineInterval.FifteenMinutes => BitgetFuturesKlineInterval.FifteenMinutes,
            CoreKlineInterval.ThirtyMinutes => BitgetFuturesKlineInterval.ThirtyMinutes,
            CoreKlineInterval.OneHour => BitgetFuturesKlineInterval.OneHour,
            CoreKlineInterval.FourHour => BitgetFuturesKlineInterval.FourHours,
            CoreKlineInterval.OneDay => BitgetFuturesKlineInterval.OneDay,
            CoreKlineInterval.OneWeek => BitgetFuturesKlineInterval.OneWeek,
            _ => throw new ArgumentException($"Unsupported interval: {interval}")
        };
    }

    /// <summary>
    /// Maps Core KlineInterval to Bitget StreamKlineInterval enum (for WebSocket)
    /// </summary>
    public static BitgetStreamKlineIntervalV2 MapStreamKlineInterval(CoreKlineInterval interval)
    {
        return interval switch
        {
            CoreKlineInterval.OneMinute => BitgetStreamKlineIntervalV2.OneMinute,
            CoreKlineInterval.FiveMinutes => BitgetStreamKlineIntervalV2.FiveMinutes,
            CoreKlineInterval.FifteenMinutes => BitgetStreamKlineIntervalV2.FifteenMinutes,
            CoreKlineInterval.ThirtyMinutes => BitgetStreamKlineIntervalV2.ThirtyMinutes,
            CoreKlineInterval.OneHour => BitgetStreamKlineIntervalV2.OneHour,
            CoreKlineInterval.FourHour => BitgetStreamKlineIntervalV2.FourHours,
            CoreKlineInterval.OneDay => BitgetStreamKlineIntervalV2.OneDay,
            CoreKlineInterval.OneWeek => BitgetStreamKlineIntervalV2.OneWeek,
            _ => throw new ArgumentException($"Unsupported interval: {interval}")
        };
    }

    /// <summary>
    /// Maps Core KlineInterval to Bitget interval string (legacy)
    /// </summary>
    public static string MapKlineIntervalToString(CoreKlineInterval interval)
    {
        return interval switch
        {
            CoreKlineInterval.OneMinute => "1m",
            CoreKlineInterval.FiveMinutes => "5m",
            CoreKlineInterval.FifteenMinutes => "15m",
            CoreKlineInterval.ThirtyMinutes => "30m",
            CoreKlineInterval.OneHour => "1H",
            CoreKlineInterval.FourHour => "4H",
            CoreKlineInterval.OneDay => "1D",
            CoreKlineInterval.OneWeek => "1W",
            _ => throw new ArgumentException($"Unsupported interval: {interval}")
        };
    }

    /// <summary>
    /// Maps TradeDirection to order side string
    /// </summary>
    public static string MapTradeDirectionToString(TradeDirection direction)
    {
        return direction == TradeDirection.Long ? "buy" : "sell";
    }

    /// <summary>
    /// Maps Core MarginType to Bitget V2 MarginMode enum
    /// </summary>
    public static BitgetMarginMode MapMarginType(CoreMarginType marginType)
    {
        return marginType == CoreMarginType.Isolated
            ? BitgetMarginMode.IsolatedMargin
            : BitgetMarginMode.CrossMargin;
    }

    /// <summary>
    /// Maps Core MarginType to Bitget margin mode string (legacy)
    /// </summary>
    public static string MapMarginTypeToString(CoreMarginType marginType)
    {
        return marginType == CoreMarginType.Isolated ? "isolated" : "crossed";
    }

    /// <summary>
    /// Maps Bitget margin mode string to Core MarginType
    /// </summary>
    public static CoreMarginType MapMarginMode(string marginMode)
    {
        return marginMode?.ToLower() == "isolated" ? CoreMarginType.Isolated : CoreMarginType.Cross;
    }
}
