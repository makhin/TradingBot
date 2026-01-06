using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using DotNetEnv;
using ComplexBot.Utils;
using Serilog;
using FluentValidation;
using ComplexBot.Configuration.Validation;

namespace ComplexBot.Configuration;

public class ConfigurationService
{
    private const string DefaultConfigFile = "appsettings.json";
    private const string UserConfigFile = "appsettings.user.json";

    private readonly IConfiguration _configuration;
    private BotConfiguration _currentConfig;

    public ConfigurationService()
    {
        LoadEnvironmentVariables();
        _configuration = BuildConfiguration();
        _currentConfig = LoadConfiguration();
    }

    private static void LoadEnvironmentVariables()
    {
        // Load .env file if it exists
        var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
        if (File.Exists(envPath))
        {
            Env.Load(envPath);

            // Determine if using testnet or mainnet
            var useTestnetStr = Environment.GetEnvironmentVariable("TRADING_BinanceApi__UseTestnet");
            var useTestnet = string.IsNullOrEmpty(useTestnetStr) ||
                             useTestnetStr.Equals("true", StringComparison.OrdinalIgnoreCase);

            // Map environment variables to nested configuration structure
            // Support both BINANCE_API_KEY format and BINANCE_TESTNET_KEY/BINANCE_MAINNET_KEY format
            string? apiKey;
            string? apiSecret;

            if (useTestnet)
            {
                apiKey = Environment.GetEnvironmentVariable("BINANCE_TESTNET_KEY") ??
                         Environment.GetEnvironmentVariable("BINANCE_API_KEY");
                apiSecret = Environment.GetEnvironmentVariable("BINANCE_TESTNET_SECRET") ??
                            Environment.GetEnvironmentVariable("BINANCE_API_SECRET");
            }
            else
            {
                apiKey = Environment.GetEnvironmentVariable("BINANCE_MAINNET_KEY") ??
                         Environment.GetEnvironmentVariable("BINANCE_API_KEY");
                apiSecret = Environment.GetEnvironmentVariable("BINANCE_MAINNET_SECRET") ??
                            Environment.GetEnvironmentVariable("BINANCE_API_SECRET");
            }

            if (!string.IsNullOrEmpty(apiKey))
            {
                Environment.SetEnvironmentVariable("BinanceApi__ApiKey", apiKey);
            }

            if (!string.IsNullOrEmpty(apiSecret))
            {
                Environment.SetEnvironmentVariable("BinanceApi__ApiSecret", apiSecret);
            }

            // Map Telegram settings if present
            var telegramToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
            var telegramChatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");

            Log.Debug("Telegram env vars - BotToken: {HasToken}, ChatId: {ChatId}",
                !string.IsNullOrEmpty(telegramToken) ? "SET" : "NOT SET",
                telegramChatId ?? "NOT SET");

            if (!string.IsNullOrEmpty(telegramToken))
            {
                Environment.SetEnvironmentVariable("Telegram__BotToken", telegramToken);
                Log.Debug("Set Telegram__BotToken from environment");
            }

            if (!string.IsNullOrEmpty(telegramChatId))
            {
                Environment.SetEnvironmentVariable("Telegram__ChatId", telegramChatId);
                Log.Debug("Set Telegram__ChatId from environment: {ChatId}", telegramChatId);
            }

            // Always enable Telegram if both token and chat ID are provided
            if (!string.IsNullOrEmpty(telegramToken) && !string.IsNullOrEmpty(telegramChatId))
            {
                Environment.SetEnvironmentVariable("Telegram__Enabled", "true");
                Log.Information("Telegram auto-enabled from .env file");
            }
        }
    }

    private static IConfiguration BuildConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(DefaultConfigFile, optional: false, reloadOnChange: true)
            .AddJsonFile(UserConfigFile, optional: true, reloadOnChange: true)
            .AddEnvironmentVariables(); // Environment variables override JSON settings

        return builder.Build();
    }

    private BotConfiguration LoadConfiguration()
    {
        var config = new BotConfiguration();
        _configuration.Bind(config);
        ApplyLegacyEnsembleWeights(config);
        ValidateConfiguration(config);
        return config;
    }

    public BotConfiguration GetConfiguration() => _currentConfig;

    public void ReloadConfiguration()
    {
        _currentConfig = LoadConfiguration();
    }

    public void SaveUserConfiguration(BotConfiguration config)
    {
        var json = JsonSerializer.Serialize(config, CreateJsonSerializerOptions(writeIndented: true));
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
        return JsonSerializer.Deserialize<BotConfiguration>(json, CreateJsonSerializerOptions(writeIndented: false))
               ?? new BotConfiguration();
    }

    private static JsonSerializerOptions CreateJsonSerializerOptions(bool writeIndented)
    {
        return new JsonSerializerOptions
        {
            WriteIndented = writeIndented,
            Converters =
            {
                new StrategyWeightsJsonConverter(),
                new JsonStringEnumConverter()
            }
        };
    }

    private void ApplyLegacyEnsembleWeights(BotConfiguration config)
    {
        var weightsSection = _configuration.GetSection("Ensemble:StrategyWeights");
        if (!weightsSection.Exists())
        {
            return;
        }

        foreach (var child in weightsSection.GetChildren())
        {
            if (!StrategyWeightKeyMapper.TryGetStrategyKind(child.Key, out var kind))
            {
                continue;
            }

            if (!decimal.TryParse(child.Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var weight))
            {
                continue;
            }

            config.Ensemble.StrategyWeights[kind] = weight;
        }
    }

    private static void ValidateConfiguration(BotConfiguration config)
    {
        var validator = new BotConfigurationValidator();
        var result = validator.Validate(config);

        if (result.IsValid)
        {
            return;
        }

        foreach (var error in result.Errors)
        {
            Log.Error("Configuration validation error: {Property} - {Message}", error.PropertyName, error.ErrorMessage);
        }

        throw new ValidationException("Configuration validation failed.", result.Errors);
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

        var updated = SettingsPrompts.BuildRiskSettings(current);

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

        var updated = SettingsPrompts.BuildStrategySettings(current);

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
