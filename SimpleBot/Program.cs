using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SimpleBot.Services;
using SimpleBot.Models;

namespace SimpleBot;

class Program
{
    static async Task Main(string[] args)
    {
        // Set invariant culture to ensure decimal numbers use dot separator
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        // Load configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var config = new BotConfiguration();
        configuration.Bind(config);

        // Validate configuration
        if (string.IsNullOrEmpty(config.Binance.ApiKey) || config.Binance.ApiKey == "YOUR_BINANCE_API_KEY_HERE")
        {
            Console.WriteLine("❌ Error: Please set your Binance API key in appsettings.json");
            return;
        }
        if (string.IsNullOrEmpty(config.Binance.ApiSecret) || config.Binance.ApiSecret == "YOUR_BINANCE_API_SECRET_HERE")
        {
            Console.WriteLine("❌ Error: Please set your Binance API secret in appsettings.json");
            return;
        }

        Console.WriteLine("🤖 Trading Bot Starting...");
        Console.WriteLine($"📈 Symbol: {config.Trading.Symbol}");
        Console.WriteLine($"📊 Strategy: {StrategyFactory.GetStrategyName(config.Strategy.Type)}");
        Console.WriteLine($"⚠️  {(config.Binance.UseTestnet ? "TESTNET MODE - No real money!" : "LIVE MODE - Real money trading!")}");
        Console.WriteLine();

        using var binance = new BinanceService(config.Binance.ApiKey, config.Binance.ApiSecret, config.Binance.UseTestnet);

        IStrategy strategy;
        try
        {
            strategy = StrategyFactory.CreateStrategy(config.Strategy);
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            return;
        }

        try
        {
            // Check initial balance
            var quoteCurrencyBalance = await binance.GetAccountBalance(config.Trading.QuoteCurrency);
            Console.WriteLine($"💰 {config.Trading.QuoteCurrency} Balance: {quoteCurrencyBalance:F2}");
            Console.WriteLine();

            // Subscribe to price updates
            await binance.SubscribeToPriceUpdates(config.Trading.Symbol, async (marketData) =>
            {
                var signal = strategy.AnalyzePrice(marketData, config.Trading.MinTradeAmount);

                if (signal != null)
                {
                    Console.WriteLine($"\n🚨 SIGNAL: {signal.Type} at {signal.Price:F2}");

                    if (signal.Type == SignalType.Buy)
                    {
                        var quoteCcyBalance = await binance.GetAccountBalance(config.Trading.QuoteCurrency);
                        if (quoteCcyBalance >= config.Trading.MinTradeAmount)
                        {
                            var success = await binance.PlaceMarketBuyOrder(signal.Symbol, signal.Quantity);
                            if (success)
                            {
                                var quoteCcy = await binance.GetAccountBalance(config.Trading.QuoteCurrency);
                                var cryptoCcy = await binance.GetAccountBalance(config.Trading.CryptoCurrency);
                                Console.WriteLine($"💰 Current Balance: {config.Trading.QuoteCurrency}={quoteCcy:F2}, {config.Trading.CryptoCurrency}={cryptoCcy:F6}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"⚠️  Insufficient {config.Trading.QuoteCurrency} balance: {quoteCcyBalance:F2}");
                        }
                    }
                    else if (signal.Type == SignalType.Sell)
                    {
                        var cryptoCcyBalance = await binance.GetAccountBalance(config.Trading.CryptoCurrency);
                        if (cryptoCcyBalance >= signal.Quantity)
                        {
                            var success = await binance.PlaceMarketSellOrder(signal.Symbol, signal.Quantity);
                            if (success)
                            {
                                var quoteCcy = await binance.GetAccountBalance(config.Trading.QuoteCurrency);
                                var cryptoCcy = await binance.GetAccountBalance(config.Trading.CryptoCurrency);
                                Console.WriteLine($"💰 Current Balance: {config.Trading.QuoteCurrency}={quoteCcy:F2}, {config.Trading.CryptoCurrency}={cryptoCcy:F6}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"⚠️  Insufficient {config.Trading.CryptoCurrency} balance: {cryptoCcyBalance:F8}");
                        }
                    }

                    Console.WriteLine();
                }
            });

            Console.WriteLine("✅ Bot is running. Press Ctrl+C to stop...\n");
            await Task.Delay(Timeout.Infinite);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
        }
    }
}