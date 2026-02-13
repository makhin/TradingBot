using SignalBot.Models;

namespace SignalBot.Services.Trading;

/// <summary>
/// Interface for managing signal positions
/// </summary>
public interface IPositionManager
{
    Task SavePositionAsync(SignalPosition position, CancellationToken ct = default);
    Task<SignalPosition?> GetPositionAsync(Guid positionId, CancellationToken ct = default);
    Task<SignalPosition?> GetPositionBySymbolAsync(string symbol, CancellationToken ct = default);
    Task<List<SignalPosition>> GetOpenPositionsAsync(CancellationToken ct = default);

    Task HandleTargetHitAsync(
        SignalPosition position,
        int targetIndex,
        decimal fillPrice,
        CancellationToken ct = default);

    Task HandleStopLossHitAsync(
        SignalPosition position,
        decimal fillPrice,
        CancellationToken ct = default);

    Task HandlePositionClosedExternallyAsync(
        SignalPosition position,
        decimal exitPrice,
        PositionCloseReason closeReason,
        CancellationToken ct = default);

    Task UpdatePositionAsync(SignalPosition position, CancellationToken ct = default);
}
