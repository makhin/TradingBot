using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using DotNetEnv;
using ComplexBot.Utils;
using ComplexBot.Services.Trading;
using Serilog;

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
            case "multipair":
                EditMultiPairFilters();
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
            RiskPerTradePercent = SpectreHelpers.AskDecimal("Risk per trade [green](%)[/]", current.RiskPerTradePercent, min: 0.1m, max: 10m),
            MaxPortfolioHeatPercent = SpectreHelpers.AskDecimal("Max portfolio heat [green](%)[/]", current.MaxPortfolioHeatPercent, min: 1m, max: 100m),
            MaxDrawdownPercent = SpectreHelpers.AskDecimal("Max drawdown circuit breaker [green](%)[/]", current.MaxDrawdownPercent, min: 5m, max: 100m),
            MaxDailyDrawdownPercent = SpectreHelpers.AskDecimal("Max daily drawdown [green](%)[/]", current.MaxDailyDrawdownPercent, min: 1m, max: 20m),
            AtrStopMultiplier = SpectreHelpers.AskDecimal("ATR stop multiplier", current.AtrStopMultiplier, min: 0.5m, max: 10m),
            TakeProfitMultiplier = SpectreHelpers.AskDecimal("Take profit ratio (reward:risk)", current.TakeProfitMultiplier, min: 0.5m, max: 10m),
            MinimumEquityUsd = SpectreHelpers.AskDecimal("Minimum equity USD", current.MinimumEquityUsd, min: 1m, max: 1000000m)
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

    private void EditMultiPairFilters()
    {
        var settings = _currentConfig.MultiPairLiveTrading;

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Multi-pair filters:")
                .AddChoices("Edit existing filter", "Add filter", "Remove filter", "Back")
        );

        if (action == "Back")
            return;

        var filters = settings.TradingPairs
            .Where(p => p.Role == StrategyRole.Filter)
            .ToList();

        if (action != "Add filter" && filters.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No filter pairs configured yet.[/]");
            return;
        }

        if (action == "Remove filter")
        {
            var selected = SelectFilter(filters);
            if (selected == null)
                return;

            settings.TradingPairs.Remove(selected);
            UpdateSection("MultiPairLiveTrading", settings);
            return;
        }

        var target = action == "Add filter"
            ? new TradingPairConfig { Role = StrategyRole.Filter }
            : SelectFilter(filters);

        if (target == null)
            return;

        ConfigureFilter(target);

        if (action == "Add filter")
        {
            settings.TradingPairs.Add(target);
        }

        UpdateSection("MultiPairLiveTrading", settings);
    }

    private static TradingPairConfig? SelectFilter(List<TradingPairConfig> filters)
    {
        var options = filters
            .Select((p, index) => new
            {
                Pair = p,
                Label = $"{index + 1}. {p.Symbol} | {p.Strategy} | {p.Interval} | {p.FilterMode?.ToString() ?? "-"}"
            })
            .ToList();

        var selectedLabel = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select filter:")
                .AddChoices(options.Select(o => o.Label))
        );

        return options.First(o => o.Label == selectedLabel).Pair;
    }

    private static void ConfigureFilter(TradingPairConfig config)
    {
        config.Role = StrategyRole.Filter;
        config.Symbol = AnsiConsole.Ask("Symbol:", config.Symbol);
        config.Interval = AnsiConsole.Ask("Interval (e.g., OneHour, FourHour):", config.Interval);

        var strategy = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Filter strategy:")
                .AddChoices("RSI", "ADX", "TRENDALIGNMENT")
        );

        config.Strategy = strategy;

        var mode = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Filter mode:")
                .AddChoices("Confirm", "Veto", "Score")
        );

        config.FilterMode = Enum.Parse<FilterMode>(mode, ignoreCase: true);

        if (strategy == "RSI")
        {
            ConfigureRsiThresholds(config);
        }
        else if (strategy == "ADX")
        {
            ConfigureAdxThresholds(config);
        }
        else
        {
            config.RsiOverbought = null;
            config.RsiOversold = null;
            config.AdxMinThreshold = null;
            config.AdxStrongThreshold = null;
        }
    }

    private static void ConfigureRsiThresholds(TradingPairConfig config)
    {
        var overbought = SpectreHelpers.AskDecimal(
            "RSI overbought",
            config.RsiOverbought ?? 70m,
            min: 50m,
            max: 95m);

        var oversold = SpectreHelpers.AskDecimal(
            "RSI oversold",
            config.RsiOversold ?? 30m,
            min: 5m,
            max: 50m);

        if (oversold >= overbought)
        {
            oversold = Math.Max(5m, overbought - 1m);
            AnsiConsole.MarkupLine("[yellow]RSI oversold adjusted to be below overbought.[/]");
        }

        config.RsiOverbought = overbought;
        config.RsiOversold = oversold;
        config.AdxMinThreshold = null;
        config.AdxStrongThreshold = null;
    }

    private static void ConfigureAdxThresholds(TradingPairConfig config)
    {
        var minAdx = SpectreHelpers.AskDecimal(
            "ADX min threshold",
            config.AdxMinThreshold ?? 20m,
            min: 5m,
            max: 60m);

        var strongDefault = config.AdxStrongThreshold ?? Math.Max(minAdx + 10m, minAdx);
        var strongAdx = SpectreHelpers.AskDecimal(
            "ADX strong threshold",
            strongDefault,
            min: minAdx,
            max: 80m);

        if (strongAdx < minAdx)
        {
            strongAdx = minAdx;
            AnsiConsole.MarkupLine("[yellow]ADX strong threshold adjusted to min value.[/]");
        }

        config.AdxMinThreshold = minAdx;
        config.AdxStrongThreshold = strongAdx;
        config.RsiOverbought = null;
        config.RsiOversold = null;
    }
}
