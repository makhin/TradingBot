using Spectre.Console;
using ComplexBot.Configuration;
using ComplexBot.Utils;
using TradingBot.Core.Models;
using TradingBot.Core.RiskManagement;
using ComplexBot.Services.Strategies;
using TradingBot.Core.Utils;
using Serilog;

namespace ComplexBot;

class SettingsService
{
    private readonly ConfigurationService _configService;
    private const string UpdatePromptText = "Save these settings?";
    private const string PromptHintText = "[grey]Press Enter to keep current value shown in brackets[/]\n";

    private static readonly SettingsPromptMetadata RiskPrompt = new(
        SectionName: "RiskManagement",
        UseDefaultsPrompt: "Use saved risk settings?",
        Title: "Risk Management Settings");

    private static readonly SettingsPromptMetadata StrategyPrompt = new(
        SectionName: "Strategy",
        UseDefaultsPrompt: "Use saved strategy settings?",
        Title: "Strategy Settings");

    public SettingsService(ConfigurationService configService)
    {
        _configService = configService;
    }

    public RiskSettings GetRiskSettings()
    {
        var config = _configService.GetConfiguration();
        return PromptSettings(
            config.RiskManagement,
            SettingsPrompts.BuildRiskSettings,
            settings => settings.ToRiskSettings(),
            RiskPrompt,
            updated => _configService.UpdateSection(RiskPrompt.SectionName, updated));
    }

    public StrategySettings GetStrategySettings()
    {
        var config = _configService.GetConfiguration();
        return PromptSettings(
            config.Strategy,
            SettingsPrompts.BuildStrategySettings,
            settings => settings.ToStrategySettings(),
            StrategyPrompt,
            updated => _configService.UpdateSection(StrategyPrompt.SectionName, updated));
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

    private TResult PromptSettings<TConfig, TResult>(
        TConfig current,
        Func<TConfig, TConfig> promptFactory,
        Func<TConfig, TResult> toRuntime,
        SettingsPromptMetadata metadata,
        Action<TConfig> updateSection)
    {
        // In non-interactive mode (when TRADING_MODE is set), always use saved settings
        var tradingModeEnv = Environment.GetEnvironmentVariable("TRADING_MODE");
        var isInteractive = string.IsNullOrEmpty(tradingModeEnv) && AnsiConsole.Profile.Capabilities.Interactive;

        if (!isInteractive)
        {
            return toRuntime(current);
        }

        var useDefaults = AnsiConsole.Confirm(metadata.UseDefaultsPrompt, defaultValue: true);

        if (useDefaults)
        {
            return toRuntime(current);
        }

        AnsiConsole.MarkupLine($"\n[yellow]{metadata.Title}[/]");
        AnsiConsole.MarkupLine(PromptHintText);

        var updated = promptFactory(current);

        if (AnsiConsole.Confirm(UpdatePromptText, defaultValue: true))
        {
            updateSection(updated);
        }

        return toRuntime(updated);
    }

    private sealed record SettingsPromptMetadata(string SectionName, string UseDefaultsPrompt, string Title);
}
