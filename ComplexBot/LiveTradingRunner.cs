using Binance.Net.Enums;
using Spectre.Console;
using ComplexBot.Models;
using ComplexBot.Configuration;
using ComplexBot.Services.Backtesting;
using ComplexBot.Services.Trading;
using ComplexBot.Services.Notifications;
using ComplexBot.Services.Strategies;
using ComplexBot.Utils;

namespace ComplexBot;

class LiveTradingRunner
{
    private readonly ConfigurationService _configService;
    private readonly SettingsService _settingsService;

    public LiveTradingRunner(ConfigurationService configService, SettingsService settingsService)
    {
        _configService = configService;
        _settingsService = settingsService;
    }

    public async Task RunLiveTrading(bool paperTrade)
    {
        // Check if running in interactive mode
        var isInteractive = AnsiConsole.Profile.Capabilities.Interactive;

        if (!paperTrade)
        {
            if (isInteractive)
            {
                var confirm = AnsiConsole.Prompt(
                    new ConfirmationPrompt("[red]⚠️ REAL MONEY MODE - Are you absolutely sure?[/]")
                    { DefaultValue = false }
                );
                if (!confirm) return;
            }
            else
            {
                // Non-interactive mode - check environment variable for confirmation
                var confirmEnv = Environment.GetEnvironmentVariable("CONFIRM_LIVE_TRADING");
                if (confirmEnv != "yes")
                {
                    AnsiConsole.MarkupLine("[red]✗ REAL MONEY MODE requires CONFIRM_LIVE_TRADING=yes environment variable[/]");
                    return;
                }
                AnsiConsole.MarkupLine("[yellow]⚠️ REAL MONEY MODE - Confirmed via environment variable[/]");
            }
        }

        AnsiConsole.MarkupLine($"\n[yellow]═══ {(paperTrade ? "PAPER" : "LIVE")} TRADING MODE ═══[/]\n");

        var config = _configService.GetConfiguration();
        var apiKey = config.BinanceApi.ApiKey;
        var apiSecret = config.BinanceApi.ApiSecret;
        var useTestnet = config.BinanceApi.UseTestnet;

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
        {
            AnsiConsole.MarkupLine("[red]✗ API keys not configured![/]");
            AnsiConsole.MarkupLine("[yellow]Please configure API keys via:[/] Configuration Settings → API Keys");
            if (useTestnet)
            {
                AnsiConsole.MarkupLine("[grey]Get testnet keys at: https://testnet.binance.vision/[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[grey]Get API keys at: https://www.binance.com/en/my/settings/api-management[/]");
            }
            return;
        }

        AnsiConsole.MarkupLine($"[grey]Using {(useTestnet ? "Testnet" : "Live")} API keys from configuration[/]\n");

        string symbol;
        string interval;
        string tradingMode;
        decimal initialCapital;

        if (isInteractive)
        {
            // Interactive mode - ask user
            symbol = AnsiConsole.Ask("Symbol:", config.LiveTrading.Symbol);
            interval = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"Interval (current: [green]{config.LiveTrading.Interval}[/]):")
                    .AddChoices("1h", "4h", "1d")
            );
            tradingMode = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"Trading mode (current: [green]{config.LiveTrading.TradingMode}[/]):")
                    .AddChoices("Spot (no margin)", "Futures/Margin")
            );
            initialCapital = SpectreHelpers.AskDecimal($"Initial capital [green](USDT)[/]", config.LiveTrading.InitialCapital, min: 1m);
        }
        else
        {
            // Non-interactive mode - use config values
            symbol = config.LiveTrading.Symbol;
            interval = config.LiveTrading.Interval;
            tradingMode = config.LiveTrading.TradingMode == "Spot" ? "Spot (no margin)" : "Futures/Margin";
            initialCapital = config.LiveTrading.InitialCapital;

            AnsiConsole.MarkupLine("[grey]Auto-configured from settings:[/]");
            AnsiConsole.MarkupLine($"  Symbol: [green]{symbol}[/]");
            AnsiConsole.MarkupLine($"  Interval: [green]{interval}[/]");
            AnsiConsole.MarkupLine($"  Trading Mode: [green]{tradingMode}[/]");
            AnsiConsole.MarkupLine($"  Initial Capital: [green]{initialCapital} USDT[/]\n");
        }

        var riskSettings = _settingsService.GetRiskSettings();
        var strategySettings = _settingsService.GetStrategySettings();

        TelegramNotifier? telegram = null;
        if (config.Telegram.Enabled && !string.IsNullOrWhiteSpace(config.Telegram.BotToken))
        {
            telegram = new TelegramNotifier(config.Telegram.BotToken, config.Telegram.ChatId);
            AnsiConsole.MarkupLine("[green]✓[/] Telegram notifications enabled (from config)\n");
        }
        else if (isInteractive && AnsiConsole.Confirm("Enable Telegram notifications?", defaultValue: false))
        {
            AnsiConsole.MarkupLine("[yellow]Please configure Telegram via:[/] Configuration Settings → Telegram Notifications");
        }

        var liveSettings = new LiveTraderSettings
        {
            Symbol = symbol,
            Interval = KlineIntervalExtensions.Parse(interval),
            InitialCapital = initialCapital,
            UseTestnet = useTestnet,
            PaperTrade = paperTrade,
            TradingMode = tradingMode == "Spot (no margin)" ? TradingMode.Spot : TradingMode.Futures
        };

        var strategy = new AdxTrendStrategy(strategySettings);
        using var trader = new BinanceLiveTrader(
            apiKey, apiSecret, strategy, riskSettings, liveSettings, telegram);

        var signalTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Time")
            .AddColumn("Signal")
            .AddColumn("Price")
            .AddColumn("Reason");

        trader.OnSignal += signal =>
        {
            signalTable.AddRow(
                DateTime.UtcNow.ToString("HH:mm:ss"),
                signal.Type.ToString(),
                $"{signal.Price:F2}",
                signal.Reason ?? ""
            );
        };

        AnsiConsole.MarkupLine("\n[green]Starting trader... Press Ctrl+C to stop[/]\n");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await trader.StartAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        await trader.StopAsync();

        if (signalTable.Rows.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[yellow]Signals Generated[/]"));
            AnsiConsole.Write(signalTable);
        }
    }
}
