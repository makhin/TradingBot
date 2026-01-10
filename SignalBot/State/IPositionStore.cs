namespace SignalBot.State;

/// <summary>
/// Interface for persisting positions
/// </summary>
/// <typeparam name="T">Position type</typeparam>
public interface IPositionStore<T> where T : class
{
    Task SavePositionAsync(T position, CancellationToken ct = default);
    Task<T?> GetPositionAsync(Guid id, CancellationToken ct = default);
    Task<T?> GetPositionBySymbolAsync(string symbol, CancellationToken ct = default);
    Task<List<T>> GetOpenPositionsAsync(CancellationToken ct = default);
    Task<List<T>> GetAllPositionsAsync(CancellationToken ct = default);
    Task DeletePositionAsync(Guid id, CancellationToken ct = default);
}
