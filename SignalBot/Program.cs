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
using SignalBot.Services.Statistics;
using TradingBot.Binance.Common;
using TradingBot.Binance.Common.Interfaces;
using TradingBot.Binance.Futures;
using TradingBot.Binance.Futures.Interfaces;
using TradingBot.Core.Notifications;
using TradingBot.Core.RiskManagement;
using Serilog;
using DotNetEnv;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using Serilog.Sinks.Elasticsearch;
using Serilog.Sinks.Grafana.Loki;
using SignalBot.Telemetry;
using TradingBot.Binance.Common.Models;
using Binance.Net.Objects.Options;

namespace SignalBot;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Load environment variables
        Env.Load();

        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables("TRADING_")
            .Build();

        // Configure Serilog
        Log.Logger = BuildLogger(configuration);

        try
        {
            Log.Information("SignalBot starting up...");

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
        ConfigureOpenTelemetry(services, configuration);

        // Resilience policies
        services.AddSingleton<IAsyncPolicy<ExecutionResult>>(_ =>
            Policy<ExecutionResult>
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    RetryPolicySettings.RetryCount,
                    attempt => TimeSpan.FromSeconds(attempt),
                    (outcome, _, retryCount, context) =>
                    {
                        if (outcome.Exception is null)
                        {
                            return;
                        }

                        if (context.TryGetValue("OnRetry", out var callback) &&
                            callback is Action<Exception, int> onRetry)
                        {
                            onRetry(outcome.Exception, retryCount);
                        }
                    }));

        // Binance clients
        services.AddSingleton(sp =>
        {
            var binanceSettings = sp.GetRequiredService<IOptions<BinanceApiSettings>>().Value;
            return new BinanceRestClient(options =>
            {
                ConfigureBinanceOptions(binanceSettings, options);
            });
        });

        services.AddSingleton(sp =>
        {
            var binanceSettings = sp.GetRequiredService<IOptions<BinanceApiSettings>>().Value;
            return new BinanceSocketClient(options =>
            {
                ConfigureBinanceOptions(binanceSettings, options);
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

        services.AddSingleton<ITradeStatisticsStore>(sp =>
            new JsonTradeStatisticsStore(
                sp.GetRequiredService<IOptions<SignalBotSettings>>().Value.State.StatisticsPath,
                sp.GetRequiredService<ILogger>()));

        services.AddSingleton<ITradeStatisticsService>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<SignalBotSettings>>().Value.Statistics;
            var windows = settings.Windows
                .Select(window => new TradeStatisticsWindow
                {
                    Name = window.Name,
                    Duration = window.Duration
                })
                .ToList();

            if (windows.Count == 0)
            {
                windows.AddRange(new[]
                {
                    new TradeStatisticsWindow { Name = "24h", Duration = TimeSpan.FromHours(24) },
                    new TradeStatisticsWindow { Name = "7d", Duration = TimeSpan.FromDays(7) },
                    new TradeStatisticsWindow { Name = "30d", Duration = TimeSpan.FromDays(30) }
                });
            }

            return new TradeStatisticsService(
                sp.GetRequiredService<ITradeStatisticsStore>(),
                windows,
                sp.GetRequiredService<ILogger>());
        });

        // Signal processing
        services.AddSingleton<ISignalMessageParser, DefaultSignalParser>();
        services.AddSingleton<ISignalMessageParser, BitcoinBulletsParser>();
        services.AddSingleton<ISignalMessageParser, FatPigParser>();
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
                    settings.TelegramCommandRetry,
                    sp.GetRequiredService<ILogger>());
            });
        }

        // Main runner
        services.AddSingleton<SignalBotRunner>();
    }

    private static ILogger BuildLogger(IConfiguration configuration)
    {
        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", "SignalBot")
            .WriteTo.Console()
            .WriteTo.File("logs/signalbot-.txt", rollingInterval: RollingInterval.Day);

        var seqUrl = configuration["Serilog:Seq:Url"];
        if (!string.IsNullOrWhiteSpace(seqUrl))
        {
            loggerConfiguration.WriteTo.Seq(seqUrl);
        }

        var lokiUrl = configuration["Serilog:Loki:Url"];
        if (!string.IsNullOrWhiteSpace(lokiUrl))
        {
            loggerConfiguration.WriteTo.GrafanaLoki(lokiUrl);
        }

        var elasticUrl = configuration["Serilog:Elasticsearch:Url"];
        if (!string.IsNullOrWhiteSpace(elasticUrl))
        {
            var indexFormat = configuration["Serilog:Elasticsearch:IndexFormat"] ?? "signalbot-logs-{0:yyyy.MM}";
            loggerConfiguration.WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri(elasticUrl))
            {
                AutoRegisterTemplate = true,
                IndexFormat = indexFormat
            });
        }

        return loggerConfiguration.CreateLogger();
    }

    private static void ConfigureOpenTelemetry(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("SignalBot"))
                    .AddSource(SignalBotTelemetry.ActivitySourceName);

                var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    builder.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
                }

                if (configuration.GetValue("OpenTelemetry:ConsoleExporter", false))
                {
                    builder.AddConsoleExporter();
                }
            });
    }

    private static void ConfigureBinanceOptions(
        BinanceApiSettings settings,
        BinanceRestOptions options)
    {
        options.ApiCredentials = new CryptoExchange.Net.Authentication.ApiCredentials(
            settings.ApiKey,
            settings.ApiSecret);

        if (settings.UseTestnet)
        {
            options.Environment = Binance.Net.BinanceEnvironment.Testnet;
        }
    }

    private static void ConfigureBinanceOptions(
        BinanceApiSettings settings,
        BinanceSocketOptions options)
    {
        options.ApiCredentials = new CryptoExchange.Net.Authentication.ApiCredentials(
            settings.ApiKey,
            settings.ApiSecret);

        if (settings.UseTestnet)
        {
            options.Environment = Binance.Net.BinanceEnvironment.Testnet;
        }
    }
}

public class BinanceApiSettings
{
    public bool UseTestnet { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
}
