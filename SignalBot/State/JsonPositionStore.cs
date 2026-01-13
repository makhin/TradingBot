using SignalBot.Models;
using Serilog;

namespace SignalBot.State;

/// <summary>
/// JSON file-based position store with domain-specific filtering
/// </summary>
public class JsonPositionStore : JsonFileStore<SignalPosition>, IPositionStore<SignalPosition>
{
    public JsonPositionStore(string filePath, ILogger? logger = null)
        : base(filePath, logger ?? Log.ForContext<JsonPositionStore>())
    {
    }

    public async Task SavePositionAsync(SignalPosition position, CancellationToken ct = default)
    {
        await AddOrUpdateAsync(position, p => p.Id, ct);
        Logger.Debug("Saved position {PositionId} for {Symbol}", position.Id, position.Symbol);
    }

    public async Task<SignalPosition?> GetPositionAsync(Guid id, CancellationToken ct = default)
    {
        return await GetAsync(p => p.Id == id, ct);
    }

    public async Task<SignalPosition?> GetPositionBySymbolAsync(string symbol, CancellationToken ct = default)
    {
        var positions = await LoadAllAsync(ct);
        return positions
            .Where(p => p.Symbol == symbol && p.Status is PositionStatus.Open or PositionStatus.PartialClosed)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefault();
    }

    public async Task<List<SignalPosition>> GetOpenPositionsAsync(CancellationToken ct = default)
    {
        var positions = await LoadAllAsync(ct);
        return positions
            .Where(p => p.Status is PositionStatus.Open or PositionStatus.PartialClosed)
            .OrderByDescending(p => p.CreatedAt)
            .ToList();
    }

    public async Task<List<SignalPosition>> GetAllPositionsAsync(CancellationToken ct = default)
    {
        return await GetAllAsync(ct);
    }

    public async Task DeletePositionAsync(Guid id, CancellationToken ct = default)
    {
        await DeleteAsync(p => p.Id == id, ct);
        Logger.Debug("Deleted position {PositionId}", id);
    }
}
