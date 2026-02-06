using Bybit.Net.Clients;
using TradingBot.Core.Models;
using Serilog;

namespace TradingBot.Bybit.Futures;

/// <summary>
/// Bybit Futures User Data Stream listener implementation
/// NOTE: This is a stub implementation - Bybit WebSocket API needs further investigation
/// </summary>
public class BybitOrderUpdateListener
{
    private readonly BybitSocketClient _socketClient;
    private readonly ILogger _logger;

    public bool IsSubscribed => false; // Stub implementation

    public BybitOrderUpdateListener(
        BybitSocketClient socketClient,
        ILogger? logger = null)
    {
        _socketClient = socketClient;
        _logger = logger ?? Log.ForContext<BybitOrderUpdateListener>();
    }

    public async Task<IDisposable?> SubscribeToOrderUpdatesAsync(
        Action<OrderUpdate> onOrderUpdate,
        CancellationToken ct = default)
    {
        _logger.Warning("BybitOrderUpdateListener.SubscribeToOrderUpdatesAsync is not fully implemented");
        // TODO: Implement actual Bybit WebSocket subscription
        // Bybit V5 WebSocket requires investigation of proper API usage
        return await Task.FromResult<IDisposable?>(null);
    }

    public async Task<IDisposable?> SubscribeToPositionUpdatesAsync(
        Action<PositionUpdate> onPositionUpdate,
        CancellationToken ct = default)
    {
        _logger.Warning("BybitOrderUpdateListener.SubscribeToPositionUpdatesAsync is not fully implemented");
        // TODO: Implement actual Bybit WebSocket subscription
        return await Task.FromResult<IDisposable?>(null);
    }

    public async Task<IDisposable?> SubscribeToAccountUpdatesAsync(
        Action<AccountUpdate> onAccountUpdate,
        CancellationToken ct = default)
    {
        _logger.Warning("BybitOrderUpdateListener.SubscribeToAccountUpdatesAsync is not fully implemented");
        // TODO: Implement actual Bybit WebSocket subscription
        return await Task.FromResult<IDisposable?>(null);
    }

    public async Task UnsubscribeAllAsync()
    {
        _logger.Information("Unsubscribed from all Bybit updates");
        await Task.CompletedTask;
    }
}
