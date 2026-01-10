using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Binance.Net.Clients;
using SignalBot.Configuration;
using SignalBot.Models;
using SignalBot.Services.Commands;
using SignalBot.Services.Monitoring;
using SignalBot.Services.Telegram;
using SignalBot.Services.Trading;
using SignalBot.Services.Validation;
using SignalBot.State;
using TradingBot.Binance.Common;
using TradingBot.Binance.Common.Interfaces;
using TradingBot.Binance.Futures;
using TradingBot.Binance.Futures.Interfaces;
using TradingBot.Core.Notifications;
using TradingBot.Core.RiskManagement;
using Serilog;
using DotNetEnv;

namespace SignalBot;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Load environment variables
        Env.Load();

        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File("logs/signalbot-.txt", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            Log.Information("SignalBot starting up...");

            // Build configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            // Load settings
            var signalBotSettings = new SignalBotSettings();
            configuration.GetSection("SignalBot").Bind(signalBotSettings);

            var binanceSettings = new BinanceApiSettings();
            configuration.GetSection("BinanceApi").Bind(binanceSettings);

            // Override from environment
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRADING_BinanceApi__UseTestnet")))
            {
                binanceSettings.UseTestnet = bool.Parse(Environment.GetEnvironmentVariable("TRADING_BinanceApi__UseTestnet")!);
            }

            // Select API keys based on testnet setting
            if (binanceSettings.UseTestnet)
            {
                binanceSettings.ApiKey = Environment.GetEnvironmentVariable("BINANCE_TESTNET_KEY") ?? binanceSettings.ApiKey;
                binanceSettings.ApiSecret = Environment.GetEnvironmentVariable("BINANCE_TESTNET_SECRET") ?? binanceSettings.ApiSecret;
            }
            else
            {
                binanceSettings.ApiKey = Environment.GetEnvironmentVariable("BINANCE_MAINNET_KEY") ?? binanceSettings.ApiKey;
                binanceSettings.ApiSecret = Environment.GetEnvironmentVariable("BINANCE_MAINNET_SECRET") ?? binanceSettings.ApiSecret;
            }

            // Telegram settings from environment
            if (int.TryParse(Environment.GetEnvironmentVariable("TELEGRAM_API_ID"), out var apiId))
                signalBotSettings.Telegram.ApiId = apiId;

            var apiHash = Environment.GetEnvironmentVariable("TELEGRAM_API_HASH");
            if (!string.IsNullOrEmpty(apiHash))
                signalBotSettings.Telegram.ApiHash = apiHash;

            var phone = Environment.GetEnvironmentVariable("TELEGRAM_PHONE");
            if (!string.IsNullOrEmpty(phone))
                signalBotSettings.Telegram.PhoneNumber = phone;

            var botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
            if (!string.IsNullOrEmpty(botToken))
                signalBotSettings.Notifications.TelegramBotToken = botToken;

            var chatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
            if (!string.IsNullOrEmpty(chatId))
                signalBotSettings.Notifications.TelegramChatId = chatId;

            // Override EnableFuturesTrading from environment if specified
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRADING_SignalBot__EnableFuturesTrading")))
            {
                signalBotSettings.EnableFuturesTrading = 
                    bool.Parse(Environment.GetEnvironmentVariable("TRADING_SignalBot__EnableFuturesTrading")!);
            }

            Log.Information("Using Binance {Mode} API", binanceSettings.UseTestnet ? "Testnet" : "Mainnet");
            Log.Information("Futures Trading: {Status}", signalBotSettings.EnableFuturesTrading ? "ENABLED" : "DISABLED");

            // Build service provider
            var services = new ServiceCollection();
            ConfigureServices(services, signalBotSettings, binanceSettings);
            var serviceProvider = services.BuildServiceProvider();

            // Create runner
            var runner = serviceProvider.GetRequiredService<SignalBotRunner>();

            // Setup graceful shutdown
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                Log.Information("Shutdown signal received");
                e.Cancel = true;
                cts.Cancel();
            };

            // Start SignalBot
            await runner.StartAsync(cts.Token);

            // Wait for shutdown signal
            Log.Information("SignalBot is running. Press Ctrl+C to stop.");

            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Log.Information("Shutting down...");
            }

            // Stop SignalBot
            await runner.StopAsync();

            Log.Information("SignalBot shutdown complete");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "SignalBot terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static void ConfigureServices(
        IServiceCollection services,
        SignalBotSettings signalBotSettings,
        BinanceApiSettings binanceSettings)
    {
        // Settings
        services.AddSingleton(signalBotSettings);
        services.AddSingleton(signalBotSettings.Telegram);
        services.AddSingleton(signalBotSettings.Trading);
        services.AddSingleton(signalBotSettings.RiskOverride);
        services.AddSingleton(signalBotSettings.PositionSizing);
        services.AddSingleton(signalBotSettings.DuplicateHandling);
        services.AddSingleton(signalBotSettings.Entry);
        services.AddSingleton(signalBotSettings.Cooldown);

        // Logging
        services.AddSingleton<ILogger>(Log.Logger);

        // Binance clients
        var restClient = new BinanceRestClient(options =>
        {
            options.ApiCredentials = new CryptoExchange.Net.Authentication.ApiCredentials(
                binanceSettings.ApiKey,
                binanceSettings.ApiSecret);

            if (binanceSettings.UseTestnet)
            {
                options.Environment = Binance.Net.BinanceEnvironment.Testnet;
            }
        });

        var socketClient = new BinanceSocketClient(options =>
        {
            options.ApiCredentials = new CryptoExchange.Net.Authentication.ApiCredentials(
                binanceSettings.ApiKey,
                binanceSettings.ApiSecret);

            if (binanceSettings.UseTestnet)
            {
                options.Environment = Binance.Net.BinanceEnvironment.Testnet;
            }
        });

        services.AddSingleton(restClient);
        services.AddSingleton(socketClient);

        // Binance services
        services.AddSingleton<IBinanceFuturesClient>(sp =>
            new BinanceFuturesClient(
                sp.GetRequiredService<BinanceRestClient>(),
                sp.GetRequiredService<ILogger>()));

        services.AddSingleton<IFuturesOrderExecutor>(sp =>
            new FuturesOrderExecutor(
                sp.GetRequiredService<BinanceRestClient>(),
                new ExecutionValidator(0.5m), // 0.5% max slippage
                sp.GetRequiredService<ILogger>()));

        services.AddSingleton<IOrderUpdateListener>(sp =>
            new FuturesOrderUpdateListener(
                sp.GetRequiredService<BinanceSocketClient>(),
                sp.GetRequiredService<BinanceRestClient>(),
                sp.GetRequiredService<ILogger>()));

        // Risk management
        services.AddSingleton<IRiskManager>(sp =>
        {
            var riskSettings = new RiskSettings
            {
                RiskPerTradePercent = signalBotSettings.RiskOverride.RiskPerTradePercent,
                MaxDrawdownPercent = signalBotSettings.RiskOverride.MaxDrawdownPercent,
                MaxDailyDrawdownPercent = signalBotSettings.RiskOverride.MaxDailyLossPercent
            };

            return new RiskManager(riskSettings, 10000m); // Will be updated with actual balance
        });

        // State persistence
        services.AddSingleton<IPositionStore<SignalPosition>>(sp =>
            new JsonPositionStore(
                signalBotSettings.State.StatePath,
                sp.GetRequiredService<ILogger>()));

        // Signal processing
        services.AddSingleton<SignalParser>();
        services.AddSingleton<ISignalValidator>(sp =>
            new SignalValidator(
                signalBotSettings.RiskOverride,
                sp.GetRequiredService<ILogger>()));

        // Trading
        services.AddSingleton<IPositionManager, PositionManager>();
        services.AddSingleton<ISignalTrader, SignalTrader>();

        // Monitoring
        services.AddSingleton<IOrderMonitor, OrderMonitor>();

        // Bot control
        services.AddSingleton<BotController>();
        services.AddSingleton<IBotCommands, TelegramBotCommands>();
        services.AddSingleton<Services.CooldownManager>();

        // Telegram
        services.AddSingleton<ITelegramSignalListener>(sp =>
            new TelegramSignalListener(
                signalBotSettings.Telegram,
                sp.GetRequiredService<SignalParser>(),
                sp.GetRequiredService<ILogger>()));

        // Notifications (optional)
        if (!string.IsNullOrEmpty(signalBotSettings.Notifications.TelegramBotToken))
        {
            services.AddSingleton<INotifier>(sp =>
                new TelegramNotifier(
                    signalBotSettings.Notifications.TelegramBotToken,
                    long.Parse(signalBotSettings.Notifications.TelegramChatId),
                    sp.GetRequiredService<ILogger>()));
        }

        // Command handler (optional - requires bot token separate from notifier)
        // For now, commands go through the same notifier bot token
        // To enable: set a separate bot token in config for commands
        if (!string.IsNullOrEmpty(signalBotSettings.Notifications.TelegramBotToken) &&
            !string.IsNullOrEmpty(signalBotSettings.Notifications.TelegramChatId))
        {
            services.AddSingleton<TelegramCommandHandler>(sp =>
                new TelegramCommandHandler(
                    sp.GetRequiredService<IBotCommands>(),
                    signalBotSettings.Notifications.TelegramBotToken,
                    long.Parse(signalBotSettings.Notifications.TelegramChatId),
                    sp.GetRequiredService<ILogger>()));
        }

        // Main runner
        services.AddSingleton<SignalBotRunner>();
    }
}

public class BinanceApiSettings
{
    public bool UseTestnet { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
}
