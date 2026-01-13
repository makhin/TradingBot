using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace SignalBot.State;

/// <summary>
/// Generic JSON file-based storage for single persisted object (not a collection)
/// Provides thread-safe load/save operations with file locking
/// </summary>
/// <typeparam name="T">Type of singleton entity to persist</typeparam>
public class JsonSingletonStore<T> where T : class, new()
{
    private readonly string _filePath;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonSingletonStore(string filePath, ILogger? logger = null)
    {
        _filePath = filePath;
        _logger = logger ?? Log.ForContext(GetType());

        // Ensure directory exists
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// Load the singleton entity from file
    /// Returns a new instance if file doesn't exist or is invalid
    /// </summary>
    public async Task<T> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_filePath))
            {
                return new T();
            }

            var json = await File.ReadAllTextAsync(_filePath, ct);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new T();
            }

            var entity = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return entity ?? new T();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error loading {Type} from {FilePath}", typeof(T).Name, _filePath);
            return new T();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Save the singleton entity to file
    /// </summary>
    public async Task SaveAsync(T entity, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.Serialize(entity, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json, ct);
            _logger.Debug("Saved {Type} to {FilePath}", typeof(T).Name, _filePath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error saving {Type} to {FilePath}", typeof(T).Name, _filePath);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Update the entity using a transformation function
    /// Loads, transforms, and saves atomically
    /// </summary>
    public async Task UpdateAsync(Func<T, T> transformation, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var current = await LoadInternalAsync(ct);
            var updated = transformation(current);
            await SaveInternalAsync(updated, ct);
            _logger.Debug("Updated {Type}", typeof(T).Name);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Internal load without locking (assumes caller holds lock)
    /// </summary>
    private async Task<T> LoadInternalAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
        {
            return new T();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new T();
            }

            var entity = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return entity ?? new T();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error loading {Type} from {FilePath}", typeof(T).Name, _filePath);
            return new T();
        }
    }

    /// <summary>
    /// Internal save without locking (assumes caller holds lock)
    /// </summary>
    private async Task SaveInternalAsync(T entity, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(entity, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json, ct);
    }

    protected ILogger Logger => _logger;
}
