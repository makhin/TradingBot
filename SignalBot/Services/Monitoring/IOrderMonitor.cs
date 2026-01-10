using SignalBot.Models;

namespace SignalBot.Services.Monitoring;

/// <summary>
/// Interface for monitoring order execution via WebSocket
/// </summary>
public interface IOrderMonitor
{
    /// <summary>
    /// Starts monitoring orders for all open positions
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops monitoring
    /// </summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Event raised when a target is hit
    /// </summary>
    event Action<Guid, int, decimal>? OnTargetHit; // positionId, targetIndex, fillPrice

    /// <summary>
    /// Event raised when stop loss is hit
    /// </summary>
    event Action<Guid, decimal>? OnStopLossHit; // positionId, fillPrice

    /// <summary>
    /// Whether the monitor is currently active
    /// </summary>
    bool IsMonitoring { get; }
}
