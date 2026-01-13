using System.Text.Json;
using System.Text.Json.Serialization;
using SignalBot.Models;
using Serilog;

namespace SignalBot.State;

/// <summary>
/// JSON file-based trade statistics store.
/// </summary>
public class JsonTradeStatisticsStore : ITradeStatisticsStore
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

    public JsonTradeStatisticsStore(string filePath, ILogger? logger = null)
    {
        _filePath = filePath;
        _logger = logger ?? Log.ForContext<JsonTradeStatisticsStore>();

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task<TradeStatisticsState> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_filePath))
            {
                return new TradeStatisticsState();
            }

            var json = await File.ReadAllTextAsync(_filePath, ct);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new TradeStatisticsState();
            }

            var state = JsonSerializer.Deserialize<TradeStatisticsState>(json, JsonOptions);
            return state ?? new TradeStatisticsState();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error loading trade statistics from {FilePath}", _filePath);
            return new TradeStatisticsState();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(TradeStatisticsState state, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.Serialize(state, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error saving trade statistics to {FilePath}", _filePath);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }
}
