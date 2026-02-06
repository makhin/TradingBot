namespace TradingBot.Core.Exchanges;

/// <summary>
/// Factory interface for creating exchange-specific implementations
/// Enables runtime selection of exchange based on configuration
/// </summary>
public interface IExchangeFactory
{
    /// <summary>
    /// Creates a futures client for the specified exchange
    /// </summary>
    IFuturesExchangeClient CreateFuturesClient(ExchangeType exchange);

    /// <summary>
    /// Creates an order executor for the specified exchange
    /// </summary>
    IFuturesOrderExecutor CreateOrderExecutor(ExchangeType exchange);

    /// <summary>
    /// Creates an order update listener for the specified exchange
    /// </summary>
    IExchangeOrderUpdateListener CreateOrderUpdateListener(ExchangeType exchange);

    /// <summary>
    /// Creates a kline listener for the specified exchange
    /// </summary>
    IExchangeKlineListener CreateKlineListener(ExchangeType exchange);
}
