using Binance.Net.Enums;
using Spectre.Console;
using ComplexBot.Models;
using ComplexBot.Configuration;
using ComplexBot.Configuration.Trading;
using ComplexBot.Services.Backtesting;
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

        // Check if multi-pair trading is enabled
        var config = _configService.GetConfiguration();
        if (config.MultiPairLiveTrading?.Enabled == true)
        {
            Log.Information("Multi-pair trading enabled - routing to multi-pair mode");
            await RunMultiPairTrading(paperTrade, isInteractive);
            return;
        }

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
        string interval;
        string tradingMode;
        decimal initialCapital;

        if (isInteractive)
        {
            Log.Debug("Interactive mode - prompting user for trading parameters");
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
            Log.Information("User selected - Symbol: {Symbol}, Interval: {Interval}, Mode: {TradingMode}, Capital: {Capital} USDT",
                symbol, interval, tradingMode, initialCapital);
        }
        else
        {
            Log.Debug("Non-interactive mode - using configuration values");
            // Non-interactive mode - use config values
            symbol = config.LiveTrading.Symbol;
            // Convert enum name (e.g., "FourHour") to short format (e.g., "4h")
            interval = ConvertIntervalToShortFormat(config.LiveTrading.Interval);
            tradingMode = config.LiveTrading.TradingMode == "Spot" ? "Spot (no margin)" : "Futures/Margin";
            initialCapital = config.LiveTrading.InitialCapital;

            Log.Information("Auto-configured - Symbol: {Symbol}, Interval: {Interval}, Mode: {TradingMode}, Capital: {Capital} USDT",
                symbol, interval, tradingMode, initialCapital);

            AnsiConsole.MarkupLine("[grey]Auto-configured from settings:[/]");
            AnsiConsole.MarkupLine($"  Symbol: [green]{symbol}[/]");
            AnsiConsole.MarkupLine($"  Interval: [green]{interval}[/]");
            AnsiConsole.MarkupLine($"  Trading Mode: [green]{tradingMode}[/]");
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

        var liveSettings = new LiveTraderSettings
        {
            Symbol = symbol,
            Interval = KlineIntervalExtensions.Parse(interval),
            InitialCapital = initialCapital,
            UseTestnet = useTestnet,
            PaperTrade = paperTrade,
            TradingMode = tradingMode == "Spot (no margin)" ? TradingMode.Spot : TradingMode.Futures
        };

        Log.Information("Initializing ADX Trend Strategy");
        var strategy = new AdxTrendStrategy(strategySettings);
        Log.Information("Creating BinanceLiveTrader instance");
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
            Log.Information("SIGNAL: {SignalType} at {Price:F2} - {Reason}",
                signal.Type, signal.Price, signal.Reason ?? "No reason provided");
            signalTable.AddRow(
                DateTime.UtcNow.ToString("HH:mm:ss"),
                signal.Type.ToString(),
                $"{signal.Price:F2}",
                signal.Reason ?? ""
            );
        };

        AnsiConsole.MarkupLine("\n[green]Starting trader... Press Ctrl+C to stop[/]\n");
        Log.Information("Starting BinanceLiveTrader - Symbol: {Symbol}, Interval: {Interval}, Paper: {PaperTrade}",
            symbol, interval, paperTrade);

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

        Log.Information("=== RunLiveTrading Completed ===");
    }

    private async Task RunMultiPairTrading(bool paperTrade, bool isInteractive)
    {
        Log.Information("=== RunMultiPairTrading Started === PaperTrade: {PaperTrade}", paperTrade);

        var config = _configService.GetConfiguration();
        var multiPairSettings = config.MultiPairLiveTrading!;

        AnsiConsole.MarkupLine($"\n[yellow]═══ MULTI-PAIR {(paperTrade ? "PAPER" : "LIVE")} TRADING ═══[/]\n");

        // Display trading pairs configuration
        DisplayTradingPairs(multiPairSettings);

        // Confirmation for real money trading
        if (!paperTrade)
        {
            Log.Warning("REAL MONEY MODE requested for multi-pair trading");
            if (isInteractive)
            {
                var confirm = AnsiConsole.Prompt(
                    new ConfirmationPrompt("[red]⚠️ REAL MONEY MODE - Trade multiple pairs with real money?[/]")
                    { DefaultValue = false }
                );
                if (!confirm)
                {
                    Log.Information("User declined real money multi-pair trading");
                    return;
                }
            }
            else
            {
                var confirmEnv = Environment.GetEnvironmentVariable("CONFIRM_LIVE_TRADING");
                if (confirmEnv != "yes")
                {
                    Log.Error("REAL MONEY MODE denied - CONFIRM_LIVE_TRADING not set");
                    AnsiConsole.MarkupLine("[red]✗ Multi-pair REAL MONEY MODE requires CONFIRM_LIVE_TRADING=yes[/]");
                    return;
                }
            }
        }

        // Validate API keys
        var apiKey = config.BinanceApi.ApiKey;
        var apiSecret = config.BinanceApi.ApiSecret;
        var useTestnet = config.BinanceApi.UseTestnet;

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
        {
            Log.Error("API keys not configured");
            AnsiConsole.MarkupLine("[red]✗ API keys not configured![/]");
            return;
        }

        Log.Information("Multi-pair configuration - Pairs: {Count}, Capital: {Capital} USDT, Mode: {AllocationMode}",
            multiPairSettings.TradingPairs.Count, multiPairSettings.TotalCapital, multiPairSettings.AllocationMode);

        // Setup shared managers
        var riskSettings = _settingsService.GetRiskSettings();
        var portfolioRiskSettings = config.PortfolioRisk.ToPortfolioRiskSettings();

        // Setup Telegram notifications
        TelegramNotifier? telegram = null;
        if (config.Telegram.Enabled && !string.IsNullOrWhiteSpace(config.Telegram.BotToken))
        {
            telegram = new TelegramNotifier(config.Telegram.BotToken, config.Telegram.ChatId);
            AnsiConsole.MarkupLine("[green]✓[/] Telegram notifications enabled\n");
        }

        // Calculate capital allocation per symbol
        var primaryPairs = multiPairSettings.TradingPairs
            .Where(p => p.Role == TradingPairRole.Primary)
            .ToList();
        var capitalPerSymbol = multiPairSettings.AllocationMode == AllocationMode.Equal
            ? multiPairSettings.TotalCapital / primaryPairs.Count
            : 0m; // Will be calculated individually for Weighted mode

        // Create multi-pair trader with factory
        using var multiTrader = new MultiPairLiveTrader<BinanceLiveTrader>(
            multiPairSettings,
            portfolioRiskSettings,
            config.PortfolioRisk.CorrelationGroups,
            (pairConfig, portfolioRiskManager, sharedEquityManager) => TraderFactory.CreateBinanceTrader(
                pairConfig,
                apiKey,
                apiSecret,
                riskSettings,
                useTestnet,
                paperTrade,
                capitalPerSymbol > 0 ? capitalPerSymbol : CalculateWeightedAllocation(pairConfig, multiPairSettings),
                portfolioRiskManager,
                sharedEquityManager,
                telegram,
                config
            ),
            telegram
        );

        // Setup monitoring dashboard
        var signalTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Time")
            .AddColumn("Symbol")
            .AddColumn("Signal")
            .AddColumn("Price")
            .AddColumn("Reason");

        multiTrader.OnLog += (symbol, msg) => Log.Information("{Symbol}: {Message}", symbol, msg);

        multiTrader.OnSignal += signal =>
        {
            Log.Information("SIGNAL: {Symbol} {Type} at {Price:F2}", signal.Symbol, signal.Type, signal.Price);
            signalTable.AddRow(
                DateTime.UtcNow.ToString("HH:mm:ss"),
                signal.Symbol,
                signal.Type.ToString(),
                $"{signal.Price:F2}",
                signal.Reason ?? ""
            );
        };

        multiTrader.OnTrade += trade =>
        {
            Log.Information("TRADE: {Symbol} {Direction} - PnL: {PnL:F2}",
                trade.Symbol, trade.Direction, trade.PnL ?? 0);
        };

        multiTrader.OnPortfolioUpdate += snapshot =>
        {
            Log.Debug("Portfolio update - Equity: {Equity:F2}, Drawdown: {DD:F2}%",
                snapshot.TotalEquity, snapshot.DrawdownPercent);
        };

        AnsiConsole.MarkupLine("\n[green]Starting multi-pair trading... Press Ctrl+C to stop[/]\n");
        Log.Information("Starting MultiPairLiveTrader with {Count} symbols", multiPairSettings.TradingPairs.Count);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            Log.Warning("Ctrl+C detected - shutting down multi-pair trading");
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await multiTrader.StartAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log.Information("Multi-pair trading cancelled by user");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during multi-pair trading");
            throw;
        }

        Log.Information("Stopping multi-pair trader");
        await multiTrader.StopAsync();

        // Display final summary
        var finalSnapshot = multiTrader.GetPortfolioSnapshot();
        DisplayFinalSummary(finalSnapshot, signalTable);

        Log.Information("=== RunMultiPairTrading Completed ===");
    }

    private void DisplayTradingPairs(MultiPairLiveTradingSettings settings)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Symbol")
            .AddColumn("Interval")
            .AddColumn("Strategy")
            .AddColumn("Role")
            .AddColumn("Allocation");

        var primaryPairs = settings.TradingPairs.Where(p => p.Role == TradingPairRole.Primary).ToList();

        foreach (var pair in settings.TradingPairs)
        {
            var weight = pair.Role == TradingPairRole.Primary
                ? (settings.AllocationMode == AllocationMode.Equal
                    ? 100m / primaryPairs.Count
                    : pair.WeightPercent ?? (100m / primaryPairs.Count))
                : 0m;

            var allocation = pair.Role == TradingPairRole.Primary
                ? $"{weight:F1}% ({settings.TotalCapital * weight / 100:N0} USDT)"
                : "-";

            var roleDisplay = pair.Role == TradingPairRole.Filter
                ? $"Filter ({pair.FilterMode})"
                : pair.Role.ToString();

            table.AddRow(
                $"[cyan]{pair.Symbol}[/]",
                pair.Interval,
                pair.Strategy,
                roleDisplay,
                allocation
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"\n[grey]Total Capital: {settings.TotalCapital:N2} USDT[/]");
        AnsiConsole.MarkupLine($"[grey]Allocation Mode: {settings.AllocationMode}[/]");
        AnsiConsole.MarkupLine($"[grey]Portfolio Risk Management: {(settings.UsePortfolioRiskManager ? "Enabled" : "Disabled")}[/]\n");
    }

    private decimal CalculateWeightedAllocation(TradingPairConfig config, MultiPairLiveTradingSettings settings)
    {
        if (config.Role != TradingPairRole.Primary) return 0m;

        var primaryPairs = settings.TradingPairs.Where(p => p.Role == TradingPairRole.Primary).ToList();
        var weight = config.WeightPercent ?? (100m / primaryPairs.Count);
        return settings.TotalCapital * weight / 100m;
    }

    private void DisplayFinalSummary(PortfolioSnapshot snapshot, Table signalTable)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Multi-Pair Trading Summary[/]"));
        AnsiConsole.WriteLine();

        var summaryTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn("Value");

        summaryTable.AddRow("Total Equity", $"{snapshot.TotalEquity:N2} USDT");
        summaryTable.AddRow("Peak Equity", $"{snapshot.PeakEquity:N2} USDT");
        summaryTable.AddRow("Drawdown", $"[{(snapshot.DrawdownPercent > 10 ? "red" : "green")}]{snapshot.DrawdownPercent:F2}%[/]");
        summaryTable.AddRow("Available Capital", $"{snapshot.AvailableCapital:N2} USDT");
        summaryTable.AddRow("Active Symbols", snapshot.SymbolDetails.Count.ToString());

        AnsiConsole.Write(summaryTable);
        AnsiConsole.WriteLine();

        // Per-symbol breakdown
        if (snapshot.SymbolDetails.Any())
        {
            var symbolTable = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Symbol")
                .AddColumn("Allocated")
                .AddColumn("Current")
                .AddColumn("Unrealized P&L")
                .AddColumn("Realized P&L");

            foreach (var (symbol, info) in snapshot.SymbolDetails.OrderBy(x => x.Key))
            {
                var unrealizedColor = info.UnrealizedPnL >= 0 ? "green" : "red";
                var realizedColor = info.RealizedPnL >= 0 ? "green" : "red";

                symbolTable.AddRow(
                    $"[cyan]{symbol}[/]",
                    $"{info.AllocatedCapital:N2}",
                    $"{info.CurrentEquity:N2}",
                    $"[{unrealizedColor}]{info.UnrealizedPnL:+0.00;-0.00}[/]",
                    $"[{realizedColor}]{info.RealizedPnL:+0.00;-0.00}[/]"
                );
            }

            AnsiConsole.Write(symbolTable);
            AnsiConsole.WriteLine();
        }

        // Signals generated
        if (signalTable.Rows.Count > 0)
        {
            AnsiConsole.Write(new Rule("[yellow]Signals Generated[/]"));
            AnsiConsole.Write(signalTable);
            Log.Information("Total signals: {Count}", signalTable.Rows.Count);
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]No signals generated during session[/]");
        }
    }

    private static string ConvertIntervalToShortFormat(string interval)
    {
        // Convert from enum name format (e.g., "FourHour") to short format (e.g., "4h")
        return interval.ToLower() switch
        {
            "oneminute" => "1m",
            "fiveminutes" => "5m",
            "fifteenminutes" => "15m",
            "thirtyminutes" => "30m",
            "onehour" => "1h",
            "fourhour" => "4h",
            "oneday" => "1d",
            "oneweek" => "1w",
            // If already in short format, return as is
            "1m" or "5m" or "15m" or "30m" or "1h" or "4h" or "1d" or "1w" => interval,
            _ => "1d" // Default to 1 day
        };
    }
}
