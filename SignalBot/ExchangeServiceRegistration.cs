using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Binance.Net.Clients;
using Bybit.Net.Clients;
using Bitget.Net.Clients;
using Serilog;
using SignalBot.Configuration;
using TradingBot.Binance.Common;
using TradingBot.Binance.Futures;
using TradingBot.Binance.Futures.Adapters;
using TradingBot.Bybit.Common;
using TradingBot.Bybit.Futures;
using TradingBot.Bybit.Futures.Adapters;
using TradingBot.Bitget.Common;
using TradingBot.Bitget.Futures;
using TradingBot.Bitget.Futures.Adapters;
using TradingBot.Core.Exchanges;

namespace SignalBot;

/// <summary>
/// Extension methods for registering exchange-specific dependencies
/// </summary>
public static class ExchangeServiceRegistration
{
    /// <summary>
    /// Register Binance exchange dependencies
    /// </summary>
    public static IServiceCollection AddBinanceDependencies(this IServiceCollection services)
    {
        // Binance SDK clients
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

        // Binance implementations (register as both concrete class and interface)
        services.AddSingleton<ExecutionValidator>();

        services.AddSingleton<BinanceFuturesClient>();
        services.AddSingleton<TradingBot.Binance.Futures.Interfaces.IBinanceFuturesClient>(sp =>
            sp.GetRequiredService<BinanceFuturesClient>());

        services.AddSingleton<FuturesOrderExecutor>();
        services.AddSingleton<TradingBot.Binance.Futures.Interfaces.IFuturesOrderExecutor>(sp =>
            sp.GetRequiredService<FuturesOrderExecutor>());

        services.AddSingleton<FuturesOrderUpdateListener>();
        services.AddSingleton<TradingBot.Binance.Common.Interfaces.IOrderUpdateListener>(sp =>
            sp.GetRequiredService<FuturesOrderUpdateListener>());

        services.AddSingleton<FuturesKlineListener>();
        services.AddSingleton<TradingBot.Binance.Common.Interfaces.IKlineListener>(sp =>
            sp.GetRequiredService<FuturesKlineListener>());

        // Register Binance adapters directly as main interfaces (since it's the active exchange)
        services.AddSingleton<IFuturesExchangeClient, BinanceFuturesClientAdapter>();
        services.AddSingleton<TradingBot.Core.Exchanges.IFuturesOrderExecutor, BinanceFuturesOrderExecutorAdapter>();
        services.AddSingleton<IExchangeOrderUpdateListener, BinanceOrderUpdateListenerAdapter>();
        services.AddSingleton<IExchangeKlineListener, BinanceKlineListenerAdapter>();

        Log.Information("Binance exchange dependencies registered");
        return services;
    }

    /// <summary>
    /// Register Bybit exchange dependencies
    /// </summary>
    public static IServiceCollection AddBybitDependencies(this IServiceCollection services)
    {
        // Bybit SDK clients
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<SignalBotSettings>>().Value;
            var bybitSettings = settings.Exchange.Bybit;
            return new BybitRestClient(options =>
            {
                options.ApiCredentials = new CryptoExchange.Net.Authentication.ApiCredentials(
                    bybitSettings.ApiKey,
                    bybitSettings.ApiSecret);
                if (bybitSettings.UseTestnet)
                {
                    options.Environment = Bybit.Net.BybitEnvironment.Testnet;
                }
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
                if (bybitSettings.UseTestnet)
                {
                    options.Environment = Bybit.Net.BybitEnvironment.Testnet;
                }
            });
        });

        // Bybit implementations (no interfaces - adapters use concrete classes)
        services.AddSingleton<BybitExecutionValidator>();
        services.AddSingleton<BybitFuturesClient>();
        services.AddSingleton<BybitFuturesOrderExecutor>();
        services.AddSingleton<BybitOrderUpdateListener>();
        services.AddSingleton<BybitKlineListener>();

        // Register Bybit adapters directly as main interfaces (since it's the active exchange)
        services.AddSingleton<IFuturesExchangeClient, BybitFuturesClientAdapter>();
        services.AddSingleton<TradingBot.Core.Exchanges.IFuturesOrderExecutor, BybitFuturesOrderExecutorAdapter>();
        services.AddSingleton<IExchangeOrderUpdateListener, BybitOrderUpdateListenerAdapter>();
        services.AddSingleton<IExchangeKlineListener, BybitKlineListenerAdapter>();

        Log.Information("Bybit exchange dependencies registered");
        return services;
    }

    /// <summary>
    /// Register Bitget exchange dependencies
    /// </summary>
    public static IServiceCollection AddBitgetDependencies(this IServiceCollection services)
    {
        // Bitget SDK clients
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<SignalBotSettings>>().Value;
            var bitgetSettings = settings.Exchange.Bitget;
            return new BitgetRestClient(options =>
            {
                options.ApiCredentials = new CryptoExchange.Net.Authentication.ApiCredentials(
                    bitgetSettings.ApiKey,
                    bitgetSettings.ApiSecret,
                    bitgetSettings.ApiPassphrase);
                if (bitgetSettings.UseTestnet)
                {
                    options.Environment = Bitget.Net.BitgetEnvironment.DemoTrading;
                }
            });
        });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<SignalBotSettings>>().Value;
            var bitgetSettings = settings.Exchange.Bitget;
            return new BitgetSocketClient(options =>
            {
                options.ApiCredentials = new CryptoExchange.Net.Authentication.ApiCredentials(
                    bitgetSettings.ApiKey,
                    bitgetSettings.ApiSecret,
                    bitgetSettings.ApiPassphrase);
                if (bitgetSettings.UseTestnet)
                {
                    options.Environment = Bitget.Net.BitgetEnvironment.DemoTrading;
                }
            });
        });

        // Bitget implementations (register with interfaces where they exist)
        services.AddSingleton<BitgetFuturesClient>(sp =>
            new BitgetFuturesClient(sp.GetRequiredService<BitgetRestClient>(), sp.GetRequiredService<ILogger>()));
        services.AddSingleton<TradingBot.Bitget.Futures.Interfaces.IBitgetFuturesClient>(sp =>
            sp.GetRequiredService<BitgetFuturesClient>());

        services.AddSingleton<BitgetFuturesOrderExecutor>(sp =>
            new BitgetFuturesOrderExecutor(sp.GetRequiredService<BitgetRestClient>(), sp.GetRequiredService<ILogger>()));
        services.AddSingleton<TradingBot.Bitget.Futures.Interfaces.IBitgetFuturesOrderExecutor>(sp =>
            sp.GetRequiredService<BitgetFuturesOrderExecutor>());

        // Listeners don't have interfaces - adapters use concrete classes
        services.AddSingleton<BitgetOrderUpdateListener>(sp =>
            new BitgetOrderUpdateListener(sp.GetRequiredService<BitgetSocketClient>(), sp.GetRequiredService<ILogger>()));

        services.AddSingleton<BitgetKlineListener>(sp =>
            new BitgetKlineListener(sp.GetRequiredService<BitgetSocketClient>(), sp.GetRequiredService<ILogger>()));

        // Register Bitget adapters directly as main interfaces (since it's the active exchange)
        services.AddSingleton<IFuturesExchangeClient, BitgetFuturesClientAdapter>();
        services.AddSingleton<TradingBot.Core.Exchanges.IFuturesOrderExecutor, BitgetFuturesOrderExecutorAdapter>();
        services.AddSingleton<IExchangeOrderUpdateListener, BitgetOrderUpdateListenerAdapter>();
        services.AddSingleton<IExchangeKlineListener, BitgetKlineListenerAdapter>();

        Log.Information("Bitget exchange dependencies registered");
        return services;
    }
}
