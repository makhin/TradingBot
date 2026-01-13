using SignalBot.Models;

namespace SignalBot.State;

/// <summary>
/// Interface for persisting trade statistics.
/// </summary>
public interface ITradeStatisticsStore
{
    Task<TradeStatisticsState> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(TradeStatisticsState state, CancellationToken ct = default);
}
