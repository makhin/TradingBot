using Spectre.Console;
using ComplexBot.Configuration;
using ComplexBot.Models;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Strategies;
using ComplexBot.Utils;
using Serilog;

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

        // In non-interactive mode (when TRADING_MODE is set), always use saved settings
        var tradingModeEnv = Environment.GetEnvironmentVariable("TRADING_MODE");
        var isInteractive = string.IsNullOrEmpty(tradingModeEnv) && AnsiConsole.Profile.Capabilities.Interactive;

        if (!isInteractive)
            return current.ToRiskSettings();

        var useDefaults = AnsiConsole.Confirm("Use saved risk settings?", defaultValue: true);

        if (useDefaults)
            return current.ToRiskSettings();

        AnsiConsole.MarkupLine("\n[yellow]Risk Management Settings[/]");
        AnsiConsole.MarkupLine("[grey]Press Enter to keep current value shown in brackets[/]\n");

        var updated = SettingsPrompts.BuildRiskSettings(current);

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

        // In non-interactive mode (when TRADING_MODE is set), always use saved settings
        var tradingModeEnv = Environment.GetEnvironmentVariable("TRADING_MODE");
        var isInteractive = string.IsNullOrEmpty(tradingModeEnv) && AnsiConsole.Profile.Capabilities.Interactive;

        if (!isInteractive)
            return current.ToStrategySettings();

        var useDefaults = AnsiConsole.Confirm("Use saved strategy settings?", defaultValue: true);

        if (useDefaults)
            return current.ToStrategySettings();

        AnsiConsole.MarkupLine("\n[yellow]Strategy Settings[/]");
        AnsiConsole.MarkupLine("[grey]Press Enter to keep current value shown in brackets[/]\n");

        var updated = SettingsPrompts.BuildStrategySettings(current);

        if (AnsiConsole.Confirm("Save these settings?", defaultValue: true))
        {
            _configService.UpdateSection("Strategy", updated);
        }

        return updated.ToStrategySettings();
    }

    public (PortfolioRiskSettings Settings, Dictionary<string, string[]> CorrelationGroups) GetPortfolioRiskConfiguration()
    {
        var config = _configService.GetConfiguration();
        var correlationGroups = config.PortfolioRisk.CorrelationGroups;

        if (correlationGroups == null || correlationGroups.Count == 0)
        {
            Log.Warning("⚠️ Correlation groups are not configured in settings. Using empty configuration.");
            return (config.PortfolioRisk.ToPortfolioRiskSettings(), new Dictionary<string, string[]>());
        }

        return (config.PortfolioRisk.ToPortfolioRiskSettings(), new Dictionary<string, string[]>(correlationGroups));
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
