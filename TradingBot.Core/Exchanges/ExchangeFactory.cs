using Microsoft.Extensions.DependencyInjection;

namespace TradingBot.Core.Exchanges;

/// <summary>
/// Factory implementation for creating exchange-specific instances
/// Resolves implementations from DI container based on exchange type
/// </summary>
public class ExchangeFactory : IExchangeFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ExchangeFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IFuturesExchangeClient CreateFuturesClient(ExchangeType exchange)
    {
        return exchange switch
        {
            ExchangeType.Binance => _serviceProvider.GetRequiredKeyedService<IFuturesExchangeClient>("Binance"),
            ExchangeType.Bybit => _serviceProvider.GetRequiredKeyedService<IFuturesExchangeClient>("Bybit"),
            _ => throw new ArgumentException($"Unsupported exchange type: {exchange}", nameof(exchange))
        };
    }

    public IFuturesOrderExecutor CreateOrderExecutor(ExchangeType exchange)
    {
        return exchange switch
        {
            ExchangeType.Binance => _serviceProvider.GetRequiredKeyedService<IFuturesOrderExecutor>("Binance"),
            ExchangeType.Bybit => _serviceProvider.GetRequiredKeyedService<IFuturesOrderExecutor>("Bybit"),
            _ => throw new ArgumentException($"Unsupported exchange type: {exchange}", nameof(exchange))
        };
    }

    public IExchangeOrderUpdateListener CreateOrderUpdateListener(ExchangeType exchange)
    {
        return exchange switch
        {
            ExchangeType.Binance => _serviceProvider.GetRequiredKeyedService<IExchangeOrderUpdateListener>("Binance"),
            ExchangeType.Bybit => _serviceProvider.GetRequiredKeyedService<IExchangeOrderUpdateListener>("Bybit"),
            _ => throw new ArgumentException($"Unsupported exchange type: {exchange}", nameof(exchange))
        };
    }

    public IExchangeKlineListener CreateKlineListener(ExchangeType exchange)
    {
        return exchange switch
        {
            ExchangeType.Binance => _serviceProvider.GetRequiredKeyedService<IExchangeKlineListener>("Binance"),
            ExchangeType.Bybit => _serviceProvider.GetRequiredKeyedService<IExchangeKlineListener>("Bybit"),
            _ => throw new ArgumentException($"Unsupported exchange type: {exchange}", nameof(exchange))
        };
    }
}
