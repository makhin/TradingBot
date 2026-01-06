using Spectre.Console;
using ComplexBot.Models;
using ComplexBot.Configuration;
using ComplexBot.Services.Trading;
using ComplexBot.Services.Notifications;
using ComplexBot.Services.Strategies;
using ComplexBot.Utils;
using Serilog;

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
        Log.Information("=== RunLiveTrading Started === PaperTrade: {PaperTrade}", paperTrade);

        // Check if running in interactive mode
        // If TRADING_MODE is set, we're in non-interactive (automated) mode
        var tradingModeEnv = Environment.GetEnvironmentVariable("TRADING_MODE");
        var isInteractive = string.IsNullOrEmpty(tradingModeEnv) && AnsiConsole.Profile.Capabilities.Interactive;
        Log.Debug("Interactive mode: {IsInteractive}, TRADING_MODE env: {TradingMode}", isInteractive, tradingModeEnv ?? "not set");

        if (!paperTrade)
        {
            Log.Warning("REAL MONEY MODE requested - checking confirmation");
            if (isInteractive)
            {
                var confirm = AnsiConsole.Prompt(
                    new ConfirmationPrompt("[red]⚠️ REAL MONEY MODE - Are you absolutely sure?[/]")
                    { DefaultValue = false }
                );
                if (!confirm)
                {
                    Log.Information("User declined real money trading");
                    return;
                }
                Log.Warning("User confirmed REAL MONEY MODE via interactive prompt");
            }
            else
            {
                // Non-interactive mode - check environment variable for confirmation
                var confirmEnv = Environment.GetEnvironmentVariable("CONFIRM_LIVE_TRADING");
                if (confirmEnv != "yes")
                {
                    Log.Error("REAL MONEY MODE denied - CONFIRM_LIVE_TRADING environment variable not set to 'yes'");
                    AnsiConsole.MarkupLine("[red]✗ REAL MONEY MODE requires CONFIRM_LIVE_TRADING=yes environment variable[/]");
                    return;
                }
                Log.Warning("REAL MONEY MODE confirmed via CONFIRM_LIVE_TRADING environment variable");
                AnsiConsole.MarkupLine("[yellow]⚠️ REAL MONEY MODE - Confirmed via environment variable[/]");
            }
        }

        AnsiConsole.MarkupLine($"\n[yellow]═══ {(paperTrade ? "PAPER" : "LIVE")} TRADING MODE ═══[/]\n");

        Log.Debug("Loading configuration");
        var config = _configService.GetConfiguration();
        var apiKey = config.BinanceApi.ApiKey;
        var apiSecret = config.BinanceApi.ApiSecret;
        var useTestnet = config.BinanceApi.UseTestnet;

        Log.Debug("API configuration - UseTestnet: {UseTestnet}, ApiKey: {ApiKeyPreview}...",
            useTestnet,
            string.IsNullOrWhiteSpace(apiKey) ? "NOT SET" : apiKey.Substring(0, Math.Min(8, apiKey.Length)));

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
        {
            Log.Error("API keys not configured - cannot start trading");
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
        Log.Information("API keys validated - using {Environment} environment", useTestnet ? "Testnet" : "Live");

        string symbol;
        KlineInterval interval;
        TradingMode tradingMode;
        decimal initialCapital;

        if (isInteractive)
        {
            Log.Debug("Interactive mode - prompting user for trading parameters");
            // Interactive mode - ask user
            symbol = AnsiConsole.Ask("Symbol:", config.LiveTrading.Symbol);
            interval = AnsiConsole.Prompt(
                new SelectionPrompt<KlineInterval>()
                    .Title($"Interval (current: [green]{UiMappings.GetIntervalLabel(config.LiveTrading.Interval)}[/]):")
                    .UseConverter(UiMappings.GetIntervalLabel)
                    .AddChoices(config.App.AllowedIntervals)
            );
            tradingMode = AnsiConsole.Prompt(
                new SelectionPrompt<TradingMode>()
                    .Title($"Trading mode (current: [green]{UiMappings.GetTradingModeLabel(config.LiveTrading.TradingMode)}[/]):")
                    .UseConverter(UiMappings.GetTradingModeLabel)
                    .AddChoices(UiMappings.TradingModes)
            );
            initialCapital = SpectreHelpers.AskDecimal($"Initial capital [green](USDT)[/]", config.LiveTrading.InitialCapital, min: 1m);
            Log.Information("User selected - Symbol: {Symbol}, Interval: {Interval}, Mode: {TradingMode}, Capital: {Capital} USDT",
                symbol, UiMappings.GetIntervalLabel(interval), UiMappings.GetTradingModeLabel(tradingMode), initialCapital);
        }
        else
        {
            Log.Debug("Non-interactive mode - using configuration values");
            // Non-interactive mode - use config values
            symbol = config.LiveTrading.Symbol;
            interval = config.LiveTrading.Interval;
            tradingMode = config.LiveTrading.TradingMode;
            initialCapital = config.LiveTrading.InitialCapital;

            Log.Information("Auto-configured - Symbol: {Symbol}, Interval: {Interval}, Mode: {TradingMode}, Capital: {Capital} USDT",
                symbol, UiMappings.GetIntervalLabel(interval), UiMappings.GetTradingModeLabel(tradingMode), initialCapital);

            AnsiConsole.MarkupLine("[grey]Auto-configured from settings:[/]");
            AnsiConsole.MarkupLine($"  Symbol: [green]{symbol}[/]");
            AnsiConsole.MarkupLine($"  Interval: [green]{UiMappings.GetIntervalLabel(interval)}[/]");
            AnsiConsole.MarkupLine($"  Trading Mode: [green]{UiMappings.GetTradingModeLabel(tradingMode)}[/]");
            AnsiConsole.MarkupLine($"  Initial Capital: [green]{initialCapital} USDT[/]\n");
        }

        Log.Debug("Loading risk and strategy settings");
        var riskSettings = _settingsService.GetRiskSettings();
        var strategySettings = _settingsService.GetStrategySettings();
        Log.Information("Settings loaded - Risk per trade: {RiskPercent}%, ADX threshold: {AdxThreshold}",
            riskSettings.RiskPerTradePercent, strategySettings.AdxThreshold);

        Log.Debug("Telegram config - Enabled: {Enabled}, BotToken: {HasToken}, ChatId: {ChatId}",
            config.Telegram.Enabled,
            !string.IsNullOrWhiteSpace(config.Telegram.BotToken) ? "SET" : "NOT SET",
            config.Telegram.ChatId);

        TelegramNotifier? telegram = null;
        if (config.Telegram.Enabled && !string.IsNullOrWhiteSpace(config.Telegram.BotToken))
        {
            Log.Information("Telegram notifications enabled - ChatId: {ChatId}", config.Telegram.ChatId);
            telegram = new TelegramNotifier(config.Telegram.BotToken, config.Telegram.ChatId);
            AnsiConsole.MarkupLine("[green]✓[/] Telegram notifications enabled (from config)\n");
        }
        else if (isInteractive && AnsiConsole.Confirm("Enable Telegram notifications?", defaultValue: false))
        {
            Log.Debug("User prompted to configure Telegram notifications");
            AnsiConsole.MarkupLine("[yellow]Please configure Telegram via:[/] Configuration Settings → Telegram Notifications");
        }
        else
        {
            Log.Warning("Telegram notifications disabled - Enabled: {Enabled}, HasToken: {HasToken}, ChatId: {ChatId}",
                config.Telegram.Enabled,
                !string.IsNullOrWhiteSpace(config.Telegram.BotToken),
                config.Telegram.ChatId);
        }

        var liveSettings = config.LiveTrading.ToLiveTraderSettings() with
        {
            Symbol = symbol,
            Interval = interval,
            InitialCapital = initialCapital,
            UseTestnet = useTestnet,
            PaperTrade = paperTrade,
            TradingMode = tradingMode
        };

        Log.Information("Initializing ADX Trend Strategy");
        var strategy = new AdxTrendStrategy(strategySettings);
        Log.Information("Creating BinanceLiveTrader instance");
        await using var trader = new BinanceLiveTrader(
            apiKey, apiSecret, strategy, riskSettings, liveSettings, telegram);

        var signalTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Time")
            .AddColumn("Signal")
            .AddColumn("Price")
            .AddColumn("Reason");

        var trades = new List<Trade>();
        var tradeTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Entry")
            .AddColumn("Exit")
            .AddColumn("Side")
            .AddColumn("Qty")
            .AddColumn("Entry Price")
            .AddColumn("Exit Price")
            .AddColumn("PnL")
            .AddColumn("Reason");

        trader.OnSignal += signal =>
        {
            Log.Information("SIGNAL: {SignalType} at {Price:F2} - {Reason}",
                signal.Type, signal.Price, signal.Reason ?? "No reason provided");
            signalTable.AddRow(
                DateTime.UtcNow.ToString("HH:mm:ss"),
                signal.Type.ToString(),
                $"{signal.Price:F2}",
                signal.Reason ?? ""
            );
        };

        trader.OnTrade += trade =>
        {
            trades.Add(trade);

            if (!trade.ExitPrice.HasValue)
            {
                return;
            }

            var pnlText = trade.PnL.HasValue
                ? $"{trade.PnL.Value:F2} ({trade.PnLPercent:0.##}%)"
                : "-";

            tradeTable.AddRow(
                trade.EntryTime.ToString("HH:mm:ss"),
                trade.ExitTime?.ToString("HH:mm:ss") ?? "-",
                trade.Direction.ToString(),
                $"{trade.Quantity:F5}",
                $"{trade.EntryPrice:F2}",
                $"{trade.ExitPrice:F2}",
                pnlText,
                trade.ExitReason ?? "");
        };

        AnsiConsole.MarkupLine("\n[green]Starting trader... Press Ctrl+C to stop[/]\n");
        Log.Information("Starting BinanceLiveTrader - Symbol: {Symbol}, Interval: {Interval}, Paper: {PaperTrade}",
            symbol, UiMappings.GetIntervalLabel(interval), paperTrade);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            Log.Warning("Ctrl+C detected - initiating shutdown");
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await trader.StartAsync(cts.Token);
            Log.Information("Trader completed normally");
        }
        catch (OperationCanceledException)
        {
            Log.Information("Trader cancelled by user");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during trading session");
            throw;
        }

        Log.Information("Stopping trader");
        await trader.StopAsync();
        Log.Information("Trader stopped successfully");

        if (signalTable.Rows.Count > 0)
        {
            Log.Information("Total signals generated: {SignalCount}", signalTable.Rows.Count);
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[yellow]Signals Generated[/]"));
            AnsiConsole.Write(signalTable);
        }
        else
        {
            Log.Information("No signals generated during session");
        }

        if (tradeTable.Rows.Count > 0)
        {
            Log.Information("Total trades executed: {TradeCount}", tradeTable.Rows.Count);
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[yellow]Trades Executed[/]"));
            AnsiConsole.Write(tradeTable);
        }
        else
        {
            Log.Information("No trades executed during session");
        }

        Log.Information("=== RunLiveTrading Completed ===");
    }

}
