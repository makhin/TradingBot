using System.Text.Json;
using System.Text.Json.Serialization;
using SignalBot.Models;
using Serilog;

namespace SignalBot.State;

/// <summary>
/// JSON file-based position store
/// </summary>
public class JsonPositionStore : IPositionStore<SignalPosition>
{
    private readonly string _filePath;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonPositionStore(string filePath, ILogger? logger = null)
    {
        _filePath = filePath;
        _logger = logger ?? Log.ForContext<JsonPositionStore>();

        // Ensure directory exists
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task SavePositionAsync(SignalPosition position, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var positions = await LoadAllPositionsInternalAsync(ct);

            // Update or add position
            var existingIndex = positions.FindIndex(p => p.Id == position.Id);
            if (existingIndex >= 0)
            {
                positions[existingIndex] = position;
            }
            else
            {
                positions.Add(position);
            }

            await SaveAllPositionsInternalAsync(positions, ct);

            _logger.Debug("Saved position {PositionId} for {Symbol}", position.Id, position.Symbol);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<SignalPosition?> GetPositionAsync(Guid id, CancellationToken ct = default)
    {
        var positions = await LoadAllPositionsInternalAsync(ct);
        return positions.FirstOrDefault(p => p.Id == id);
    }

    public async Task<SignalPosition?> GetPositionBySymbolAsync(string symbol, CancellationToken ct = default)
    {
        var positions = await LoadAllPositionsInternalAsync(ct);
        return positions
            .Where(p => p.Symbol == symbol && p.Status is PositionStatus.Open or PositionStatus.PartialClosed)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefault();
    }

    public async Task<List<SignalPosition>> GetOpenPositionsAsync(CancellationToken ct = default)
    {
        var positions = await LoadAllPositionsInternalAsync(ct);
        return positions
            .Where(p => p.Status is PositionStatus.Open or PositionStatus.PartialClosed)
            .OrderByDescending(p => p.CreatedAt)
            .ToList();
    }

    public async Task<List<SignalPosition>> GetAllPositionsAsync(CancellationToken ct = default)
    {
        return await LoadAllPositionsInternalAsync(ct);
    }

    public async Task DeletePositionAsync(Guid id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var positions = await LoadAllPositionsInternalAsync(ct);
            positions.RemoveAll(p => p.Id == id);
            await SaveAllPositionsInternalAsync(positions, ct);

            _logger.Debug("Deleted position {PositionId}", id);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<SignalPosition>> LoadAllPositionsInternalAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
        {
            return new List<SignalPosition>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<SignalPosition>();
            }

            var positions = JsonSerializer.Deserialize<List<SignalPosition>>(json, JsonOptions);
            return positions ?? new List<SignalPosition>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error loading positions from {FilePath}", _filePath);
            return new List<SignalPosition>();
        }
    }

    private async Task SaveAllPositionsInternalAsync(List<SignalPosition> positions, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(positions, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error saving positions to {FilePath}", _filePath);
            throw;
        }
    }
}
