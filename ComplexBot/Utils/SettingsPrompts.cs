using Spectre.Console;
using ComplexBot.Configuration;

namespace ComplexBot.Utils;

public static class SettingsPrompts
{
    public static RiskManagementSettings BuildRiskSettings(RiskManagementSettings current)
    {
        return new RiskManagementSettings
        {
            RiskPerTradePercent = SpectreHelpers.AskDecimal("Risk per trade [green](%)[/]", current.RiskPerTradePercent, min: 0.1m, max: 10m),
            MaxPortfolioHeatPercent = SpectreHelpers.AskDecimal("Max portfolio heat [green](%)[/]", current.MaxPortfolioHeatPercent, min: 1m, max: 100m),
            MaxDrawdownPercent = SpectreHelpers.AskDecimal("Max drawdown circuit breaker [green](%)[/]", current.MaxDrawdownPercent, min: 5m, max: 100m),
            MaxDailyDrawdownPercent = SpectreHelpers.AskDecimal("Max daily drawdown [green](%)[/]", current.MaxDailyDrawdownPercent, min: 1m, max: 20m),
            AtrStopMultiplier = SpectreHelpers.AskDecimal("ATR stop multiplier", current.AtrStopMultiplier, min: 0.5m, max: 10m),
            TakeProfitMultiplier = SpectreHelpers.AskDecimal("Take profit ratio (reward:risk)", current.TakeProfitMultiplier, min: 0.5m, max: 10m),
            MinimumEquityUsd = SpectreHelpers.AskDecimal("Minimum equity USD", current.MinimumEquityUsd, min: 1m, max: 1000000m),
            DrawdownRiskPolicy = current.DrawdownRiskPolicy
        };
    }

    public static StrategyConfigSettings BuildStrategySettings(StrategyConfigSettings current)
    {
        var updated = new StrategyConfigSettings
        {
            AdxPeriod = SpectreHelpers.AskInt("ADX period", current.AdxPeriod, min: 5, max: 50),
            AdxThreshold = SpectreHelpers.AskDecimal("ADX entry threshold", current.AdxThreshold, min: 10m, max: 50m),
            AdxExitThreshold = SpectreHelpers.AskDecimal("ADX exit threshold", current.AdxExitThreshold, min: 5m, max: 40m),
            RequireAdxRising = AnsiConsole.Confirm("Require ADX rising?", current.RequireAdxRising),
            AdxSlopeLookback = SpectreHelpers.AskInt("ADX slope lookback (bars)", current.AdxSlopeLookback, min: 1, max: 20),
            FastEmaPeriod = SpectreHelpers.AskInt("Fast EMA period", current.FastEmaPeriod, min: 5, max: 50),
            SlowEmaPeriod = SpectreHelpers.AskInt("Slow EMA period", current.SlowEmaPeriod, min: 20, max: 200),
            AtrPeriod = SpectreHelpers.AskInt("ATR period", current.AtrPeriod, min: 5, max: 50),
            MinAtrPercent = SpectreHelpers.AskDecimal("Min ATR % of price", current.MinAtrPercent, min: 0m, max: 50m),
            MaxAtrPercent = SpectreHelpers.AskDecimal("Max ATR % of price", current.MaxAtrPercent, min: 0m, max: 200m),
            RequireVolumeConfirmation = AnsiConsole.Confirm("Require volume confirmation?", current.RequireVolumeConfirmation),
            VolumeThreshold = SpectreHelpers.AskDecimal("Volume spike threshold (x avg)", current.VolumeThreshold, min: 0.5m, max: 10m),
            RequireObvConfirmation = AnsiConsole.Confirm("Require OBV confirmation?", current.RequireObvConfirmation)
        };

        updated.RequireFreshTrend = current.RequireFreshTrend;
        updated.AdxFallingExitBars = current.AdxFallingExitBars;
        updated.MaxBarsInTrade = current.MaxBarsInTrade;
        updated.AtrStopMultiplier = current.AtrStopMultiplier;
        updated.TakeProfitMultiplier = current.TakeProfitMultiplier;
        updated.VolumePeriod = current.VolumePeriod;
        updated.ObvPeriod = current.ObvPeriod;
        updated.PartialExitRMultiple = current.PartialExitRMultiple;
        updated.PartialExitFraction = current.PartialExitFraction;

        return updated;
    }
}
