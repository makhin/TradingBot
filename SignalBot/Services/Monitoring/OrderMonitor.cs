using SignalBot.Models;
using SignalBot.State;
using TradingBot.Binance.Common.Interfaces;
using TradingBot.Binance.Common.Models;
using Serilog;

namespace SignalBot.Services.Monitoring;

/// <summary>
/// Monitors order execution via Binance User Data Stream
/// </summary>
public class OrderMonitor : IOrderMonitor
{
    private readonly IOrderUpdateListener _updateListener;
    private readonly IPositionStore<SignalPosition> _store;
    private readonly ILogger _logger;
    private IDisposable? _subscription;
    private bool _isMonitoring;

    public event Action<Guid, int, decimal>? OnTargetHit;
    public event Action<Guid, decimal>? OnStopLossHit;

    public bool IsMonitoring => _isMonitoring;

    public OrderMonitor(
        IOrderUpdateListener updateListener,
        IPositionStore<SignalPosition> store,
        ILogger? logger = null)
    {
        _updateListener = updateListener;
        _store = store;
        _logger = logger ?? Log.ForContext<OrderMonitor>();
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_isMonitoring)
        {
            _logger.Warning("Order monitor is already running");
            return;
        }

        _logger.Information("Starting order monitor");

        _subscription = await _updateListener.SubscribeToOrderUpdatesAsync(HandleOrderUpdate, ct);

        if (_subscription != null)
        {
            _isMonitoring = true;
            _logger.Information("Order monitor started successfully");
        }
        else
        {
            _logger.Error("Failed to start order monitor");
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!_isMonitoring)
        {
            _logger.Warning("Order monitor is not running");
            return;
        }

        _logger.Information("Stopping order monitor");

        if (_subscription != null)
        {
            _subscription.Dispose();
            _subscription = null;
        }

        await _updateListener.UnsubscribeAllAsync();

        _isMonitoring = false;
        _logger.Information("Order monitor stopped");
    }

    private async void HandleOrderUpdate(OrderUpdate update)
    {
        try
        {
            // Only process filled orders
            if (update.Status != "Filled")
            {
                _logger.Debug("Order {OrderId} status: {Status}", update.OrderId, update.Status);
                return;
            }

            _logger.Information("Order filled: {Symbol} {OrderId} @ {Price}, Qty: {Qty}",
                update.Symbol, update.OrderId, update.AveragePrice, update.QuantityFilled);

            // Find position by symbol
            var position = await _store.GetPositionBySymbolAsync(update.Symbol);
            if (position == null)
            {
                _logger.Debug("No open position found for {Symbol}, ignoring order update", update.Symbol);
                return;
            }

            // Check if this is a stop loss order
            if (position.StopLossOrderId == update.OrderId)
            {
                _logger.Information("Stop loss hit for {Symbol} @ {Price}", update.Symbol, update.AveragePrice);
                OnStopLossHit?.Invoke(position.Id, update.AveragePrice);
                return;
            }

            // Check if this is a take profit order
            for (int i = 0; i < position.TakeProfitOrderIds.Count; i++)
            {
                if (position.TakeProfitOrderIds[i] == update.OrderId)
                {
                    var targetIndex = i; // TP orders are in same order as targets
                    if (targetIndex < position.Targets.Count)
                    {
                        _logger.Information("Target {Index} hit for {Symbol} @ {Price}",
                            targetIndex + 1, update.Symbol, update.AveragePrice);
                        OnTargetHit?.Invoke(position.Id, targetIndex, update.AveragePrice);
                    }
                    return;
                }
            }

            _logger.Debug("Order {OrderId} not associated with SL or TP for position {PositionId}",
                update.OrderId, position.Id);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling order update for {Symbol} {OrderId}",
                update.Symbol, update.OrderId);
        }
    }
}
