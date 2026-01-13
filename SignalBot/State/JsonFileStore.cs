using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace SignalBot.State;

/// <summary>
/// Generic JSON file-based storage for any persisted model
/// Provides thread-safe load/save operations with file locking
/// </summary>
/// <typeparam name="T">Type of entity to persist</typeparam>
public class JsonFileStore<T> where T : class
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

    public JsonFileStore(string filePath, ILogger? logger = null)
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
    /// Load all entities from the file
    /// </summary>
    protected async Task<List<T>> LoadAllAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
        {
            return new List<T>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<T>();
            }

            var entities = JsonSerializer.Deserialize<List<T>>(json, JsonOptions);
            return entities ?? new List<T>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error loading entities from {FilePath}", _filePath);
            return new List<T>();
        }
    }

    /// <summary>
    /// Save all entities to the file
    /// </summary>
    protected async Task SaveAllAsync(List<T> entities, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(entities, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error saving entities to {FilePath}", _filePath);
            throw;
        }
    }

    /// <summary>
    /// Get all entities without locking (for read operations)
    /// </summary>
    public async Task<List<T>> GetAllAsync(CancellationToken ct = default)
    {
        return await LoadAllAsync(ct);
    }

    /// <summary>
    /// Get first entity matching the predicate
    /// </summary>
    public async Task<T?> GetAsync(Func<T, bool> predicate, CancellationToken ct = default)
    {
        var entities = await LoadAllAsync(ct);
        return entities.FirstOrDefault(predicate);
    }

    /// <summary>
    /// Get all entities matching the predicate
    /// </summary>
    public async Task<List<T>> GetManyAsync(Func<T, bool> predicate, CancellationToken ct = default)
    {
        var entities = await LoadAllAsync(ct);
        return entities.Where(predicate).ToList();
    }

    /// <summary>
    /// Add or update an entity based on key selector
    /// </summary>
    /// <param name="entity">Entity to add or update</param>
    /// <param name="keySelector">Function to extract the key for matching</param>
    /// <param name="ct">Cancellation token</param>
    public async Task AddOrUpdateAsync(T entity, Func<T, object> keySelector, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var entities = await LoadAllAsync(ct);
            var key = keySelector(entity);

            // Find existing entity by key
            var existingIndex = entities.FindIndex(e => keySelector(e).Equals(key));
            if (existingIndex >= 0)
            {
                entities[existingIndex] = entity;
            }
            else
            {
                entities.Add(entity);
            }

            await SaveAllAsync(entities, ct);
            _logger.Debug("Saved entity of type {Type} with key {Key}", typeof(T).Name, key);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Delete entities matching the predicate
    /// </summary>
    public async Task DeleteAsync(Func<T, bool> predicate, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var entities = await LoadAllAsync(ct);
            var countBefore = entities.Count;
            entities.RemoveAll(e => predicate(e));
            var removed = countBefore - entities.Count;

            if (removed > 0)
            {
                await SaveAllAsync(entities, ct);
                _logger.Debug("Deleted {Count} entities of type {Type}", removed, typeof(T).Name);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Update all entities atomically with a transformation function
    /// Useful for batch operations
    /// </summary>
    public async Task UpdateAllAsync(Func<List<T>, List<T>> transformation, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var entities = await LoadAllAsync(ct);
            var updated = transformation(entities);
            await SaveAllAsync(updated, ct);
            _logger.Debug("Updated all entities of type {Type}", typeof(T).Name);
        }
        finally
        {
            _lock.Release();
        }
    }

    protected ILogger Logger => _logger;
}
