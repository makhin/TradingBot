using TradingBot.Core.Exchanges;
using TradingBot.Core.Models;
using TradingBot.Binance.Common.Interfaces;
using BinanceOrderUpdate = TradingBot.Binance.Common.Interfaces.OrderUpdate;
using BinancePositionUpdate = TradingBot.Binance.Common.Interfaces.PositionUpdate;
using BinanceAccountUpdate = TradingBot.Binance.Common.Interfaces.AccountUpdate;

namespace TradingBot.Binance.Futures.Adapters;

/// <summary>
/// Adapter that wraps Binance IOrderUpdateListener and exposes generic IExchangeOrderUpdateListener interface
/// Converts Binance-specific update types to exchange-agnostic Core types
/// </summary>
public class BinanceOrderUpdateListenerAdapter : IExchangeOrderUpdateListener
{
    private readonly IOrderUpdateListener _binanceListener;

    public bool IsSubscribed => _binanceListener.IsSubscribed;

    public BinanceOrderUpdateListenerAdapter(IOrderUpdateListener binanceListener)
    {
        _binanceListener = binanceListener;
    }

    public Task<IDisposable?> SubscribeToOrderUpdatesAsync(
        Action<TradingBot.Core.Models.OrderUpdate> onOrderUpdate,
        CancellationToken ct = default)
    {
        return _binanceListener.SubscribeToOrderUpdatesAsync(
            binanceUpdate => onOrderUpdate(ConvertOrderUpdate(binanceUpdate)),
            ct);
    }

    public Task<IDisposable?> SubscribeToPositionUpdatesAsync(
        Action<TradingBot.Core.Models.PositionUpdate> onPositionUpdate,
        CancellationToken ct = default)
    {
        return _binanceListener.SubscribeToPositionUpdatesAsync(
            binanceUpdate => onPositionUpdate(ConvertPositionUpdate(binanceUpdate)),
            ct);
    }

    public Task<IDisposable?> SubscribeToAccountUpdatesAsync(
        Action<TradingBot.Core.Models.AccountUpdate> onAccountUpdate,
        CancellationToken ct = default)
    {
        return _binanceListener.SubscribeToAccountUpdatesAsync(
            binanceUpdate => onAccountUpdate(ConvertAccountUpdate(binanceUpdate)),
            ct);
    }

    public Task UnsubscribeAllAsync()
        => _binanceListener.UnsubscribeAllAsync();

    private static TradingBot.Core.Models.OrderUpdate ConvertOrderUpdate(BinanceOrderUpdate binanceUpdate)
    {
        return new TradingBot.Core.Models.OrderUpdate
        {
            Symbol = binanceUpdate.Symbol,
            OrderId = binanceUpdate.OrderId,
            Status = binanceUpdate.Status,
            Direction = binanceUpdate.Direction,
            Quantity = binanceUpdate.Quantity,
            Price = binanceUpdate.Price,
            AveragePrice = binanceUpdate.AveragePrice,
            QuantityFilled = binanceUpdate.QuantityFilled,
            UpdateTime = binanceUpdate.UpdateTime,
            OrderType = binanceUpdate.OrderType,
            TimeInForce = binanceUpdate.TimeInForce
        };
    }

    private static TradingBot.Core.Models.PositionUpdate ConvertPositionUpdate(BinancePositionUpdate binanceUpdate)
    {
        return new TradingBot.Core.Models.PositionUpdate
        {
            Symbol = binanceUpdate.Symbol,
            PositionAmount = binanceUpdate.PositionAmount,
            EntryPrice = binanceUpdate.EntryPrice,
            UnrealizedPnl = binanceUpdate.UnrealizedPnl,
            UpdateTime = binanceUpdate.UpdateTime,
            Side = binanceUpdate.Side
        };
    }

    private static TradingBot.Core.Models.AccountUpdate ConvertAccountUpdate(BinanceAccountUpdate binanceUpdate)
    {
        return new TradingBot.Core.Models.AccountUpdate
        {
            Asset = binanceUpdate.Asset,
            Balance = binanceUpdate.Balance,
            AvailableBalance = binanceUpdate.AvailableBalance,
            UpdateTime = binanceUpdate.UpdateTime
        };
    }
}
