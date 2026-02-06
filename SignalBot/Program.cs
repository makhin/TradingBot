using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Binance.Net.Clients;
using Bybit.Net.Clients;
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
using TradingBot.Binance.Futures;
using TradingBot.Binance.Futures.Adapters;
using TradingBot.Bybit.Common;
using TradingBot.Bybit.Futures;
using TradingBot.Bybit.Futures.Adapters;
using TradingBot.Core.Exchanges;
using TradingBot.Core.Models;
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
using Binance.Net.Objects.Options;
using Bybit.Net.Objects.Options;

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

            Log.Information("Using Exchange: {Exchange}", signalBotSettings.Exchange.ActiveExchange);
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

        // Exchange SDK clients
        // Binance
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<SignalBotSettings>>().Value;
            var binanceSettings = settings.Exchange.Binance;
            return new BinanceRestClient(options =>
            {
                options.ApiCredentials = new CryptoExchange.Net.Authentication.ApiCredentials(
                    binanceSettings.ApiKey,
                    binanceSettings.ApiSecret);
                // TODO: Configure testnet environment if needed
                // if (binanceSettings.UseTestnet)
                // {
                //     options.Environment = Binance.Net.Objects.BinanceEnvironment.Testnet;
                // }
            });
        });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<SignalBotSettings>>().Value;
            var binanceSettings = settings.Exchange.Binance;
            return new BinanceSocketClient(options =>
            {
                options.ApiCredentials = new CryptoExchange.Net.Authentication.ApiCredentials(
                    binanceSettings.ApiKey,
                    binanceSettings.ApiSecret);
                // TODO: Configure testnet environment if needed
                // if (binanceSettings.UseTestnet)
                // {
                //     options.Environment = Binance.Net.Objects.BinanceEnvironment.Testnet;
                // }
            });
        });

        // Bybit
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<SignalBotSettings>>().Value;
            var bybitSettings = settings.Exchange.Bybit;
            return new BybitRestClient(options =>
            {
                options.ApiCredentials = new CryptoExchange.Net.Authentication.ApiCredentials(
                    bybitSettings.ApiKey,
                    bybitSettings.ApiSecret);
                // TODO: Configure testnet environment if needed
                // if (bybitSettings.UseTestnet)
                // {
                //     options.Environment = Bybit.Net.Objects.BybitEnvironment.Testnet;
                // }
            });
        });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<SignalBotSettings>>().Value;
            var bybitSettings = settings.Exchange.Bybit;
            return new BybitSocketClient(options =>
            {
                options.ApiCredentials = new CryptoExchange.Net.Authentication.ApiCredentials(
                    bybitSettings.ApiKey,
                    bybitSettings.ApiSecret);
                // TODO: Configure testnet environment if needed
                // if (bybitSettings.UseTestnet)
                // {
                //     options.Environment = Bybit.Net.Objects.BybitEnvironment.Testnet;
                // }
            });
        });

        // Exchange-specific implementations
        // Binance implementations and adapters
        services.AddSingleton<BinanceFuturesClient>();
        services.AddSingleton<FuturesOrderExecutor>();
        services.AddSingleton<FuturesOrderUpdateListener>();
        services.AddSingleton<FuturesKlineListener>();

        services.AddKeyedSingleton<IFuturesExchangeClient>("Binance", (sp, _) =>
            new BinanceFuturesClientAdapter(sp.GetRequiredService<BinanceFuturesClient>()));

        services.AddKeyedSingleton<TradingBot.Core.Exchanges.IFuturesOrderExecutor>("Binance", (sp, _) =>
            new BinanceFuturesOrderExecutorAdapter(sp.GetRequiredService<FuturesOrderExecutor>()));

        services.AddKeyedSingleton<IExchangeOrderUpdateListener>("Binance", (sp, _) =>
            new BinanceOrderUpdateListenerAdapter(sp.GetRequiredService<FuturesOrderUpdateListener>()));

        services.AddKeyedSingleton<IExchangeKlineListener>("Binance", (sp, _) =>
            new BinanceKlineListenerAdapter(sp.GetRequiredService<FuturesKlineListener>()));

        // Bybit implementations and adapters
        services.AddSingleton<BybitFuturesClient>();
        services.AddSingleton<BybitFuturesOrderExecutor>();
        services.AddSingleton<BybitOrderUpdateListener>();
        services.AddSingleton<BybitKlineListener>();

        services.AddKeyedSingleton<IFuturesExchangeClient>("Bybit", (sp, _) =>
            new BybitFuturesClientAdapter(sp.GetRequiredService<BybitFuturesClient>()));

        services.AddKeyedSingleton<TradingBot.Core.Exchanges.IFuturesOrderExecutor>("Bybit", (sp, _) =>
            new BybitFuturesOrderExecutorAdapter(sp.GetRequiredService<BybitFuturesOrderExecutor>()));

        services.AddKeyedSingleton<IExchangeOrderUpdateListener>("Bybit", (sp, _) =>
            new BybitOrderUpdateListenerAdapter(sp.GetRequiredService<BybitOrderUpdateListener>()));

        services.AddKeyedSingleton<IExchangeKlineListener>("Bybit", (sp, _) =>
            new BybitKlineListenerAdapter(sp.GetRequiredService<BybitKlineListener>()));

        // Exchange factory
        services.AddSingleton<IExchangeFactory, ExchangeFactory>();

        // Active exchange instances (resolved via factory based on configuration)
        services.AddSingleton<IFuturesExchangeClient>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<SignalBotSettings>>().Value;
            var factory = sp.GetRequiredService<IExchangeFactory>();
            var exchangeType = Enum.Parse<ExchangeType>(settings.Exchange.ActiveExchange, ignoreCase: true);
            return factory.CreateFuturesClient(exchangeType);
        });

        services.AddSingleton<TradingBot.Core.Exchanges.IFuturesOrderExecutor>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<SignalBotSettings>>().Value;
            var factory = sp.GetRequiredService<IExchangeFactory>();
            var exchangeType = Enum.Parse<ExchangeType>(settings.Exchange.ActiveExchange, ignoreCase: true);
            return factory.CreateOrderExecutor(exchangeType);
        });

        services.AddSingleton<IExchangeOrderUpdateListener>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<SignalBotSettings>>().Value;
            var factory = sp.GetRequiredService<IExchangeFactory>();
            var exchangeType = Enum.Parse<ExchangeType>(settings.Exchange.ActiveExchange, ignoreCase: true);
            return factory.CreateOrderUpdateListener(exchangeType);
        });

        services.AddSingleton<IExchangeKlineListener>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<SignalBotSettings>>().Value;
            var factory = sp.GetRequiredService<IExchangeFactory>();
            var exchangeType = Enum.Parse<ExchangeType>(settings.Exchange.ActiveExchange, ignoreCase: true);
            return factory.CreateKlineListener(exchangeType);
        });

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
        services.AddSingleton<ISignalMessageParser, EmojiSignalParser>();
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
                    settings.TelegramAuthorizedUserIds,
                    settings.TelegramCommandRetry,
                    sp.GetRequiredService<IOptions<SignalBotSettings>>(),
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

}
