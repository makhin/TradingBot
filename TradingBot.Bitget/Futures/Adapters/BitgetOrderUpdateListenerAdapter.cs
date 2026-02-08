using TradingBot.Core.Exchanges;
using TradingBot.Core.Models;
using BitgetOrderUpdate = TradingBot.Bitget.Futures.Models.OrderUpdate;
using BitgetPositionUpdate = TradingBot.Bitget.Futures.Models.PositionUpdate;

namespace TradingBot.Bitget.Futures.Adapters;

/// <summary>
/// Adapter that wraps Bitget BitgetOrderUpdateListener and exposes generic IExchangeOrderUpdateListener interface
/// Converts Bitget-specific update types to exchange-agnostic Core types
/// </summary>
public class BitgetOrderUpdateListenerAdapter : IExchangeOrderUpdateListener
{
    private readonly BitgetOrderUpdateListener _bitgetListener;

    public bool IsSubscribed => _bitgetListener.IsSubscribed;

    public BitgetOrderUpdateListenerAdapter(BitgetOrderUpdateListener bitgetListener)
    {
        _bitgetListener = bitgetListener;
    }

    public Task<IDisposable?> SubscribeToOrderUpdatesAsync(
        Action<TradingBot.Core.Models.OrderUpdate> onOrderUpdate,
        CancellationToken ct = default)
    {
        return _bitgetListener.SubscribeToOrderUpdatesAsync(
            bitgetUpdate => onOrderUpdate(ConvertOrderUpdate(bitgetUpdate)),
            ct);
    }

    public Task<IDisposable?> SubscribeToPositionUpdatesAsync(
        Action<TradingBot.Core.Models.PositionUpdate> onPositionUpdate,
        CancellationToken ct = default)
    {
        return _bitgetListener.SubscribeToPositionUpdatesAsync(
            bitgetUpdate => onPositionUpdate(ConvertPositionUpdate(bitgetUpdate)),
            ct);
    }

    public Task<IDisposable?> SubscribeToAccountUpdatesAsync(
        Action<TradingBot.Core.Models.AccountUpdate> onAccountUpdate,
        CancellationToken ct = default)
    {
        // Bitget doesn't have account updates in the current implementation
        return Task.FromResult<IDisposable?>(null);
    }

    public Task UnsubscribeAllAsync()
        => _bitgetListener.UnsubscribeAllAsync();

    private static TradingBot.Core.Models.OrderUpdate ConvertOrderUpdate(BitgetOrderUpdate bitgetUpdate)
    {
        return new TradingBot.Core.Models.OrderUpdate
        {
            Symbol = bitgetUpdate.Symbol,
            OrderId = bitgetUpdate.OrderId,
            Status = bitgetUpdate.Status,
            Direction = bitgetUpdate.Side,
            Quantity = bitgetUpdate.Quantity,
            Price = bitgetUpdate.Price,
            AveragePrice = bitgetUpdate.AveragePrice,
            QuantityFilled = bitgetUpdate.FilledQuantity,
            UpdateTime = bitgetUpdate.UpdateTime,
            OrderType = "Market",
            TimeInForce = "GTC"
        };
    }

    private static TradingBot.Core.Models.PositionUpdate ConvertPositionUpdate(BitgetPositionUpdate bitgetUpdate)
    {
        var direction = bitgetUpdate.Side == PositionSide.Long ? TradeDirection.Long : TradeDirection.Short;

        return new TradingBot.Core.Models.PositionUpdate
        {
            Symbol = bitgetUpdate.Symbol,
            PositionAmount = bitgetUpdate.Quantity,
            EntryPrice = bitgetUpdate.EntryPrice,
            UnrealizedPnl = bitgetUpdate.UnrealizedPnl,
            UpdateTime = bitgetUpdate.UpdateTime,
            Side = direction
        };
    }
}
