using Spectre.Console;
using ComplexBot.Configuration;
using ComplexBot.Models;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Strategies;
using ComplexBot.Utils;

namespace ComplexBot;

class SettingsService
{
    private readonly ConfigurationService _configService;

    public SettingsService(ConfigurationService configService)
    {
        _configService = configService;
    }

    public RiskSettings GetRiskSettings()
    {
        var config = _configService.GetConfiguration();
        var current = config.RiskManagement;

        var useDefaults = AnsiConsole.Confirm("Use saved risk settings?", defaultValue: true);

        if (useDefaults)
            return current.ToRiskSettings();

        AnsiConsole.MarkupLine("\n[yellow]Risk Management Settings[/]");
        AnsiConsole.MarkupLine("[grey]Press Enter to keep current value shown in brackets[/]\n");

        var updated = new RiskManagementSettings
        {
            RiskPerTradePercent = SpectreHelpers.AskDecimal("Risk per trade [green](%)[/]", current.RiskPerTradePercent, min: 0.1m, max: 10m),
            MaxPortfolioHeatPercent = SpectreHelpers.AskDecimal("Max portfolio heat [green](%)[/]", current.MaxPortfolioHeatPercent, min: 1m, max: 100m),
            MaxDrawdownPercent = SpectreHelpers.AskDecimal("Max drawdown circuit breaker [green](%)[/]", current.MaxDrawdownPercent, min: 5m, max: 100m),
            MaxDailyDrawdownPercent = SpectreHelpers.AskDecimal("Max daily drawdown [green](%)[/]", current.MaxDailyDrawdownPercent, min: 1m, max: 20m),
            AtrStopMultiplier = SpectreHelpers.AskDecimal("ATR stop multiplier", current.AtrStopMultiplier, min: 0.5m, max: 10m),
            TakeProfitMultiplier = SpectreHelpers.AskDecimal("Take profit ratio (reward:risk)", current.TakeProfitMultiplier, min: 0.5m, max: 10m),
            MinimumEquityUsd = SpectreHelpers.AskDecimal("Minimum equity USD", current.MinimumEquityUsd, min: 1m, max: 1000000m)
        };

        if (AnsiConsole.Confirm("Save these settings?", defaultValue: true))
        {
            _configService.UpdateSection("RiskManagement", updated);
        }

        return updated.ToRiskSettings();
    }

    public StrategySettings GetStrategySettings()
    {
        var config = _configService.GetConfiguration();
        var current = config.Strategy;

        var useDefaults = AnsiConsole.Confirm("Use saved strategy settings?", defaultValue: true);

        if (useDefaults)
            return current.ToStrategySettings();

        AnsiConsole.MarkupLine("\n[yellow]Strategy Settings[/]");
        AnsiConsole.MarkupLine("[grey]Press Enter to keep current value shown in brackets[/]\n");

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

        if (AnsiConsole.Confirm("Save these settings?", defaultValue: true))
        {
            _configService.UpdateSection("Strategy", updated);
        }

        return updated.ToStrategySettings();
    }

    public void ConfigureSettings()
    {
        var section = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Which settings to configure?")
                .AddChoices(
                    "Risk Management",
                    "Strategy Parameters",
                    "Correlation Groups",
                    "Telegram Notifications",
                    "API Keys",
                    "Back to Menu")
        );

        switch (section)
        {
            case "Risk Management":
                _configService.EditInteractive("risk");
                break;
            case "Strategy Parameters":
                _configService.EditInteractive("strategy");
                break;
            case "Correlation Groups":
                _configService.EditInteractive("correlation");
                break;
            case "Telegram Notifications":
                _configService.EditInteractive("telegram");
                break;
            case "API Keys":
                _configService.EditInteractive("api");
                break;
        }
    }
}
