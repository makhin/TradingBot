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
        if (!paperTrade)
        {
            var confirm = AnsiConsole.Prompt(
                new ConfirmationPrompt("[red]⚠️ REAL MONEY MODE - Are you absolutely sure?[/]")
                { DefaultValue = false }
            );
            if (!confirm) return;
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

        var symbol = AnsiConsole.Ask("Symbol:", config.LiveTrading.Symbol);
        var interval = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"Interval (current: [green]{config.LiveTrading.Interval}[/]):")
                .AddChoices("1h", "4h", "1d")
        );
        var tradingMode = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"Trading mode (current: [green]{config.LiveTrading.TradingMode}[/]):")
                .AddChoices("Spot (no margin)", "Futures/Margin")
        );

        var riskSettings = _settingsService.GetRiskSettings();
        var strategySettings = _settingsService.GetStrategySettings();
        var initialCapital = SpectreHelpers.AskDecimal($"Initial capital [green](USDT)[/]", config.LiveTrading.InitialCapital, min: 1m);

        TelegramNotifier? telegram = null;
        if (config.Telegram.Enabled && !string.IsNullOrWhiteSpace(config.Telegram.BotToken))
        {
            telegram = new TelegramNotifier(config.Telegram.BotToken, config.Telegram.ChatId);
            AnsiConsole.MarkupLine("[green]✓[/] Telegram notifications enabled (from config)\n");
        }
        else if (AnsiConsole.Confirm("Enable Telegram notifications?", defaultValue: false))
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
