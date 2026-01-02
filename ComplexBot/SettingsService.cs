using Spectre.Console;
using ComplexBot.Configuration;
using ComplexBot.Models;
using ComplexBot.Services.RiskManagement;

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
            RiskPerTradePercent = AnsiConsole.Ask($"Risk per trade [green](%)[/] [[{current.RiskPerTradePercent}]]:", current.RiskPerTradePercent),
            MaxPortfolioHeatPercent = AnsiConsole.Ask($"Max portfolio heat [green](%)[/] [[{current.MaxPortfolioHeatPercent}]]:", current.MaxPortfolioHeatPercent),
            MaxDrawdownPercent = AnsiConsole.Ask($"Max drawdown circuit breaker [green](%)[/] [[{current.MaxDrawdownPercent}]]:", current.MaxDrawdownPercent),
            MaxDailyDrawdownPercent = AnsiConsole.Ask($"Max daily drawdown [green](%)[/] [[{current.MaxDailyDrawdownPercent}]]:", current.MaxDailyDrawdownPercent),
            AtrStopMultiplier = AnsiConsole.Ask($"ATR stop multiplier [[{current.AtrStopMultiplier}]]:", current.AtrStopMultiplier),
            TakeProfitMultiplier = AnsiConsole.Ask($"Take profit ratio (reward:risk) [[{current.TakeProfitMultiplier}]]:", current.TakeProfitMultiplier),
            MinimumEquityUsd = AnsiConsole.Ask($"Minimum equity USD [[{current.MinimumEquityUsd}]]:", current.MinimumEquityUsd)
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
            AdxPeriod = AnsiConsole.Ask($"ADX period [[{current.AdxPeriod}]]:", current.AdxPeriod),
            AdxThreshold = AnsiConsole.Ask($"ADX entry threshold [[{current.AdxThreshold}]]:", current.AdxThreshold),
            AdxExitThreshold = AnsiConsole.Ask($"ADX exit threshold [[{current.AdxExitThreshold}]]:", current.AdxExitThreshold),
            RequireAdxRising = AnsiConsole.Confirm("Require ADX rising?", current.RequireAdxRising),
            AdxSlopeLookback = AnsiConsole.Ask($"ADX slope lookback (bars) [[{current.AdxSlopeLookback}]]:", current.AdxSlopeLookback),
            FastEmaPeriod = AnsiConsole.Ask($"Fast EMA period [[{current.FastEmaPeriod}]]:", current.FastEmaPeriod),
            SlowEmaPeriod = AnsiConsole.Ask($"Slow EMA period [[{current.SlowEmaPeriod}]]:", current.SlowEmaPeriod),
            AtrPeriod = AnsiConsole.Ask($"ATR period [[{current.AtrPeriod}]]:", current.AtrPeriod),
            MinAtrPercent = AnsiConsole.Ask($"Min ATR % of price [[{current.MinAtrPercent}]]:", current.MinAtrPercent),
            MaxAtrPercent = AnsiConsole.Ask($"Max ATR % of price [[{current.MaxAtrPercent}]]:", current.MaxAtrPercent),
            RequireVolumeConfirmation = AnsiConsole.Confirm("Require volume confirmation?", current.RequireVolumeConfirmation),
            VolumeThreshold = AnsiConsole.Ask($"Volume spike threshold (x avg) [[{current.VolumeThreshold}]]:", current.VolumeThreshold),
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
