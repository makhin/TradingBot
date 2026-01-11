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
using Microsoft.Extensions.Options;

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
                .AddEnvironmentVariables("TRADING_")
                .Build();

            // Build service provider
            var services = new ServiceCollection();
            ConfigureServices(services, configuration);
            var serviceProvider = services.BuildServiceProvider();

            var signalBotSettings = serviceProvider.GetRequiredService<IOptions<SignalBotSettings>>().Value;
            var binanceSettings = serviceProvider.GetRequiredService<IOptions<BinanceApiSettings>>().Value;

            Log.Information("Using Binance {Mode} API", binanceSettings.UseTestnet ? "Testnet" : "Mainnet");
            Log.Information("Futures Trading: {Status}", signalBotSettings.EnableFuturesTrading ? "ENABLED" : "DISABLED");

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
        IConfiguration configuration)
    {
        services.AddSingleton(configuration);
        services.AddOptions();
        services.Configure<SignalBotSettings>(configuration.GetSection("SignalBot"));
        services.Configure<BinanceApiSettings>(configuration.GetSection("BinanceApi"));

        // Settings
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<SignalBotSettings>>().Value.Telegram);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<SignalBotSettings>>().Value.Trading);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<SignalBotSettings>>().Value.RiskOverride);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<SignalBotSettings>>().Value.PositionSizing);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<SignalBotSettings>>().Value.DuplicateHandling);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<SignalBotSettings>>().Value.Entry);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<SignalBotSettings>>().Value.Cooldown);

        // Logging
        services.AddSingleton<ILogger>(Log.Logger);

        // Binance clients
        services.AddSingleton(sp =>
        {
            var binanceSettings = sp.GetRequiredService<IOptions<BinanceApiSettings>>().Value;
            return new BinanceRestClient(options =>
            {
                options.ApiCredentials = new CryptoExchange.Net.Authentication.ApiCredentials(
                    binanceSettings.ApiKey,
                    binanceSettings.ApiSecret);

                if (binanceSettings.UseTestnet)
                {
                    options.Environment = Binance.Net.BinanceEnvironment.Testnet;
                }
            });
        });

        services.AddSingleton(sp =>
        {
            var binanceSettings = sp.GetRequiredService<IOptions<BinanceApiSettings>>().Value;
            return new BinanceSocketClient(options =>
            {
                options.ApiCredentials = new CryptoExchange.Net.Authentication.ApiCredentials(
                    binanceSettings.ApiKey,
                    binanceSettings.ApiSecret);

                if (binanceSettings.UseTestnet)
                {
                    options.Environment = Binance.Net.BinanceEnvironment.Testnet;
                }
            });
        });

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
            var signalBotSettings = sp.GetRequiredService<IOptions<SignalBotSettings>>().Value;
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
                sp.GetRequiredService<IOptions<SignalBotSettings>>().Value.State.StatePath,
                sp.GetRequiredService<ILogger>()));

        // Signal processing
        services.AddSingleton<SignalParser>();
        services.AddSingleton<ISignalValidator>(sp =>
            new SignalValidator(
                sp.GetRequiredService<IOptions<SignalBotSettings>>().Value.RiskOverride,
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
                sp.GetRequiredService<IOptions<SignalBotSettings>>().Value.Telegram,
                sp.GetRequiredService<SignalParser>(),
                sp.GetRequiredService<ILogger>()));

        // Notifications (optional)
        var notificationSettings = configuration.GetSection("SignalBot:Notifications").Get<NotificationSettings>()
            ?? new NotificationSettings();
        if (!string.IsNullOrEmpty(notificationSettings.TelegramBotToken))
        {
            services.AddSingleton<INotifier>(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<SignalBotSettings>>().Value.Notifications;
                return new TelegramNotifier(
                    settings.TelegramBotToken,
                    long.Parse(settings.TelegramChatId),
                    sp.GetRequiredService<ILogger>());
            });
        }

        // Command handler (optional - requires bot token separate from notifier)
        // For now, commands go through the same notifier bot token
        // To enable: set a separate bot token in config for commands
        if (!string.IsNullOrEmpty(notificationSettings.TelegramBotToken) &&
            !string.IsNullOrEmpty(notificationSettings.TelegramChatId))
        {
            services.AddSingleton<TelegramCommandHandler>(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<SignalBotSettings>>().Value.Notifications;
                return new TelegramCommandHandler(
                    sp.GetRequiredService<IBotCommands>(),
                    settings.TelegramBotToken,
                    long.Parse(settings.TelegramChatId),
                    sp.GetRequiredService<ILogger>());
            });
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
