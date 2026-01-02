using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

namespace ComplexBot.Configuration;

public class ConfigurationService
{
    private const string DefaultConfigFile = "appsettings.json";
    private const string UserConfigFile = "appsettings.user.json";

    private readonly IConfiguration _configuration;
    private BotConfiguration _currentConfig;

    public ConfigurationService()
    {
        _configuration = BuildConfiguration();
        _currentConfig = LoadConfiguration();
    }

    private static IConfiguration BuildConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(DefaultConfigFile, optional: false, reloadOnChange: true)
            .AddJsonFile(UserConfigFile, optional: true, reloadOnChange: true);

        return builder.Build();
    }

    private BotConfiguration LoadConfiguration()
    {
        var config = new BotConfiguration();
        _configuration.Bind(config);
        return config;
    }

    public BotConfiguration GetConfiguration() => _currentConfig;

    public void ReloadConfiguration()
    {
        _currentConfig = LoadConfiguration();
    }

    public void SaveUserConfiguration(BotConfiguration config)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(UserConfigFile, json);

        AnsiConsole.MarkupLine($"[green]✓[/] Configuration saved to {UserConfigFile}");
        ReloadConfiguration();
    }

    public void ResetToDefaults()
    {
        if (File.Exists(UserConfigFile))
        {
            File.Delete(UserConfigFile);
            AnsiConsole.MarkupLine($"[yellow]![/] User configuration deleted: {UserConfigFile}");
        }

        ReloadConfiguration();
        AnsiConsole.MarkupLine("[green]✓[/] Configuration reset to defaults");
    }

    public void UpdateSection<T>(string sectionName, T sectionData) where T : class
    {
        var userConfig = LoadUserConfigOrDefault();

        var property = typeof(BotConfiguration).GetProperty(sectionName);
        if (property != null && property.CanWrite)
        {
            property.SetValue(userConfig, sectionData);
            SaveUserConfiguration(userConfig);
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Unknown configuration section: {sectionName}[/]");
        }
    }

    private BotConfiguration LoadUserConfigOrDefault()
    {
        if (!File.Exists(UserConfigFile))
        {
            return _currentConfig;
        }

        var json = File.ReadAllText(UserConfigFile);
        return JsonSerializer.Deserialize<BotConfiguration>(json) ?? new BotConfiguration();
    }

    public void EditInteractive(string section)
    {
        switch (section.ToLower())
        {
            case "risk":
                EditRiskSettings();
                break;
            case "strategy":
                EditStrategySettings();
                break;
            case "correlation":
                EditCorrelationGroups();
                break;
            case "telegram":
                EditTelegramSettings();
                break;
            case "api":
                EditApiKeys();
                break;
            default:
                AnsiConsole.MarkupLine($"[red]Unknown section: {section}[/]");
                break;
        }
    }

    private void EditRiskSettings()
    {
        var current = _currentConfig.RiskManagement;

        AnsiConsole.MarkupLine("\n[yellow]Risk Management Settings[/]");
        AnsiConsole.MarkupLine("[grey]Press Enter to keep current value shown in brackets[/]\n");

        var updated = new RiskManagementSettings
        {
            RiskPerTradePercent = AnsiConsole.Ask($"Risk per trade [green](%)[/] [{current.RiskPerTradePercent}]:", current.RiskPerTradePercent),
            MaxPortfolioHeatPercent = AnsiConsole.Ask($"Max portfolio heat [green](%)[/] [{current.MaxPortfolioHeatPercent}]:", current.MaxPortfolioHeatPercent),
            MaxDrawdownPercent = AnsiConsole.Ask($"Max drawdown circuit breaker [green](%)[/] [{current.MaxDrawdownPercent}]:", current.MaxDrawdownPercent),
            MaxDailyDrawdownPercent = AnsiConsole.Ask($"Max daily drawdown [green](%)[/] [{current.MaxDailyDrawdownPercent}]:", current.MaxDailyDrawdownPercent),
            AtrStopMultiplier = AnsiConsole.Ask($"ATR stop multiplier [{current.AtrStopMultiplier}]:", current.AtrStopMultiplier),
            TakeProfitMultiplier = AnsiConsole.Ask($"Take profit ratio (reward:risk) [{current.TakeProfitMultiplier}]:", current.TakeProfitMultiplier),
            MinimumEquityUsd = AnsiConsole.Ask($"Minimum equity USD [{current.MinimumEquityUsd}]:", current.MinimumEquityUsd)
        };

        UpdateSection("RiskManagement", updated);
    }

    private void EditStrategySettings()
    {
        var current = _currentConfig.Strategy;
        var useDefaults = AnsiConsole.Confirm("Use default strategy settings?", defaultValue: true);

        if (useDefaults)
        {
            UpdateSection("Strategy", new StrategyConfigSettings());
            return;
        }

        AnsiConsole.MarkupLine("\n[yellow]Strategy Settings[/]");
        AnsiConsole.MarkupLine("[grey]Press Enter to keep current value shown in brackets[/]\n");

        var updated = new StrategyConfigSettings
        {
            AdxPeriod = AnsiConsole.Ask($"ADX period [{current.AdxPeriod}]:", current.AdxPeriod),
            AdxThreshold = AnsiConsole.Ask($"ADX entry threshold [{current.AdxThreshold}]:", current.AdxThreshold),
            AdxExitThreshold = AnsiConsole.Ask($"ADX exit threshold [{current.AdxExitThreshold}]:", current.AdxExitThreshold),
            RequireAdxRising = AnsiConsole.Confirm("Require ADX rising?", current.RequireAdxRising),
            AdxSlopeLookback = AnsiConsole.Ask($"ADX slope lookback (bars) [{current.AdxSlopeLookback}]:", current.AdxSlopeLookback),
            FastEmaPeriod = AnsiConsole.Ask($"Fast EMA period [{current.FastEmaPeriod}]:", current.FastEmaPeriod),
            SlowEmaPeriod = AnsiConsole.Ask($"Slow EMA period [{current.SlowEmaPeriod}]:", current.SlowEmaPeriod),
            AtrPeriod = AnsiConsole.Ask($"ATR period [{current.AtrPeriod}]:", current.AtrPeriod),
            MinAtrPercent = AnsiConsole.Ask($"Min ATR % of price [{current.MinAtrPercent}]:", current.MinAtrPercent),
            MaxAtrPercent = AnsiConsole.Ask($"Max ATR % of price [{current.MaxAtrPercent}]:", current.MaxAtrPercent),
            RequireVolumeConfirmation = AnsiConsole.Confirm("Require volume confirmation?", current.RequireVolumeConfirmation),
            VolumeThreshold = AnsiConsole.Ask($"Volume spike threshold (x avg) [{current.VolumeThreshold}]:", current.VolumeThreshold),
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

        UpdateSection("Strategy", updated);
    }

    private void EditCorrelationGroups()
    {
        AnsiConsole.MarkupLine("\n[yellow]Correlation Groups Configuration[/]");
        AnsiConsole.MarkupLine("[grey]Edit correlation groups in appsettings.user.json manually[/]");
        AnsiConsole.MarkupLine("[grey]Format: \"GroupName\": [\"SYMBOL1\", \"SYMBOL2\", ...][/]\n");

        var current = _currentConfig.PortfolioRisk.CorrelationGroups;
        if (current.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No correlation groups configured. Using defaults.[/]");
        }
        else
        {
            foreach (var group in current)
            {
                AnsiConsole.MarkupLine($"  [cyan]{group.Key}[/]: {string.Join(", ", group.Value)}");
            }
        }
    }

    private void EditTelegramSettings()
    {
        AnsiConsole.MarkupLine("\n[yellow]Telegram Configuration[/]");
        AnsiConsole.MarkupLine("[grey]Get bot token from @BotFather[/]");
        AnsiConsole.MarkupLine("[grey]Get chat ID from https://api.telegram.org/bot<TOKEN>/getUpdates[/]\n");

        var enabled = AnsiConsole.Confirm("Enable Telegram notifications?", _currentConfig.Telegram.Enabled);

        if (enabled)
        {
            var updated = new TelegramSettings
            {
                Enabled = true,
                BotToken = AnsiConsole.Ask("Bot Token:", _currentConfig.Telegram.BotToken),
                ChatId = AnsiConsole.Ask("Chat ID:", _currentConfig.Telegram.ChatId)
            };

            UpdateSection("Telegram", updated);
        }
        else
        {
            UpdateSection("Telegram", new TelegramSettings { Enabled = false });
        }
    }

    private void EditApiKeys()
    {
        var config = _currentConfig;

        AnsiConsole.MarkupLine("\n[yellow]Binance API Configuration[/]");
        AnsiConsole.MarkupLine("[grey]Get API keys from https://www.binance.com/en/my/settings/api-management[/]\n");

        var updated = new BinanceApiSettings
        {
            ApiKey = AnsiConsole.Ask("API Key:", config.BinanceApi.ApiKey),
            ApiSecret = AnsiConsole.Prompt(
                new TextPrompt<string>("API Secret:")
                    .Secret()),
            UseTestnet = AnsiConsole.Confirm("Use Testnet?", config.BinanceApi.UseTestnet)
        };

        UpdateSection("BinanceApi", updated);
    }
}
