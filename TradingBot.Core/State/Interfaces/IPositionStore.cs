using TradingBot.Core.State;

namespace TradingBot.Core.State;

/// <summary>
/// Interface for persisting and retrieving position information
/// </summary>
public interface IPositionStore
{
    /// <summary>
    /// Saves current position to persistent storage
    /// </summary>
    Task SavePositionAsync(SavedPosition position, CancellationToken ct = default);

    /// <summary>
    /// Loads position from persistent storage
    /// </summary>
    Task<SavedPosition?> LoadPositionAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// Removes position from persistent storage
    /// </summary>
    Task DeletePositionAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// Gets all saved positions
    /// </summary>
    Task<List<SavedPosition>> GetAllPositionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks if a position exists for the given symbol
    /// </summary>
    Task<bool> HasPositionAsync(string symbol, CancellationToken ct = default);
}
