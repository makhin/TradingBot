using System;
using System.Linq;
using System.Threading.Tasks;
using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Objects;
using Binance.Net.Objects.Options;
using Binance.Net.Enums;
using CryptoExchange.Net.Authentication;
using SimpleBot.Models;

namespace SimpleBot.Services;

public class BinanceService : IDisposable
{
    private readonly BinanceRestClient _restClient;
    private readonly BinanceSocketClient _socketClient;

    public BinanceService(string apiKey, string apiSecret, bool useTestnet = true)
    {
        _restClient = new BinanceRestClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
            if (useTestnet)
            {
                options.Environment = BinanceEnvironment.Testnet;
            }
        });

        _socketClient = new BinanceSocketClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
            if (useTestnet)
            {
                options.Environment = BinanceEnvironment.Testnet;
            }
        });
    }

    public async Task<decimal> GetCurrentPrice(string symbol)
    {
        var result = await _restClient.SpotApi.ExchangeData.GetPriceAsync(symbol);
        
        if (!result.Success)
            throw new Exception($"Failed to get price: {result.Error?.Message}");

        return result.Data.Price;
    }

    public async Task<decimal> GetAccountBalance(string asset)
    {
        var result = await _restClient.SpotApi.Account.GetAccountInfoAsync();
        
        if (!result.Success)
            throw new Exception($"Failed to get balance: {result.Error?.Message}");

        var balance = result.Data.Balances.FirstOrDefault(b => b.Asset == asset);
        return balance?.Available ?? 0;
    }

    public async Task SubscribeToPriceUpdates(string symbol, Action<MarketData> onPriceUpdate)
    {
        await _socketClient.SpotApi.ExchangeData.SubscribeToTickerUpdatesAsync(symbol, data =>
        {
            var marketData = new MarketData(
                data.Data.Symbol,
                data.Data.LastPrice,
                DateTime.UtcNow
            );
            onPriceUpdate(marketData);
        });
    }

    public async Task<bool> PlaceMarketBuyOrder(string symbol, decimal quantity)
    {
        var result = await _restClient.SpotApi.Trading.PlaceOrderAsync(
            symbol,
            Binance.Net.Enums.OrderSide.Buy,
            Binance.Net.Enums.SpotOrderType.Market,
            quoteQuantity: quantity
        );

        if (result.Success)
        {
            Console.WriteLine($"✅ Buy order placed: {result.Data.Id}");
            return true;
        }

        Console.WriteLine($"❌ Buy order failed: {result.Error?.Message}");
        return false;
    }

    public async Task<bool> PlaceMarketSellOrder(string symbol, decimal quantity)
    {
        var result = await _restClient.SpotApi.Trading.PlaceOrderAsync(
            symbol,
            Binance.Net.Enums.OrderSide.Sell,
            Binance.Net.Enums.SpotOrderType.Market,
            quantity: quantity
        );

        if (result.Success)
        {
            Console.WriteLine($"✅ Sell order placed: {result.Data.Id}");
            return true;
        }

        Console.WriteLine($"❌ Sell order failed: {result.Error?.Message}");
        return false;
    }

    public void Dispose()
    {
        _restClient?.Dispose();
        _socketClient?.Dispose();
    }
}