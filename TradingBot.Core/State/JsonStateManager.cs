using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace TradingBot.Core.State;

/// <summary>
/// Manages bot state persistence with atomic writes and backup functionality
/// </summary>
public class JsonStateManager : IStateManager<BotState>
{
    private readonly string _statePath;
    private readonly string _backupPath;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public JsonStateManager(string statePath = "bot_state.json", ILogger? logger = null)
    {
        _statePath = statePath;
        _backupPath = statePath + ".bak";
        _logger = logger ?? Log.ForContext<JsonStateManager>();
    }

    /// <summary>
    /// Saves bot state to disk with atomic write and backup
    /// </summary>
    public async Task SaveStateAsync(BotState state, CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            // Backup existing state
            if (File.Exists(_statePath))
            {
                File.Copy(_statePath, _backupPath, overwrite: true);
            }

            // Atomic write via temp file
            var tempPath = _statePath + ".tmp";
            var json = JsonSerializer.Serialize(state, CreateJsonOptions());
            await File.WriteAllTextAsync(tempPath, json, ct);

            // Atomic rename (Windows: File.Move with overwrite = true)
            File.Move(tempPath, _statePath, overwrite: true);

            _logger.Information("💾 State saved: {Positions} positions, Equity: ${Equity:F2}",
                state.OpenPositions.Count, state.CurrentEquity);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save state to {Path}", _statePath);
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Loads bot state from disk
    /// </summary>
    public async Task<BotState?> LoadStateAsync(CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_statePath))
            {
                _logger.Debug("No state file found at {Path}", _statePath);
                return null;
            }

            var json = await File.ReadAllTextAsync(_statePath, ct);
            var state = JsonSerializer.Deserialize<BotState>(json, CreateJsonOptions());

            if (state != null)
            {
                _logger.Information("📂 State loaded: {Positions} positions, Equity: ${Equity:F2}, LastUpdate: {LastUpdate}",
                    state.OpenPositions.Count, state.CurrentEquity, state.LastUpdate);
            }

            return state;
        }
        catch (JsonException ex)
        {
            _logger.Error(ex, "Failed to deserialize state file - file may be corrupted");

            // Try loading backup
            if (File.Exists(_backupPath))
            {
                _logger.Warning("Attempting to load backup state from {Path}", _backupPath);
                return await LoadBackupAsync(ct);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load state from {Path}", _statePath);
            return null;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Loads state from backup file
    /// </summary>
    public async Task<BotState?> LoadBackupAsync(CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_backupPath))
            {
                _logger.Debug("No backup state file found at {Path}", _backupPath);
                return null;
            }

            var json = await File.ReadAllTextAsync(_backupPath, ct);
            var state = JsonSerializer.Deserialize<BotState>(json, CreateJsonOptions());

            if (state != null)
            {
                _logger.Information("📂 Backup state loaded: {Positions} positions, Equity: ${Equity:F2}",
                    state.OpenPositions.Count, state.CurrentEquity);
            }

            return state;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load backup state from {Path}", _backupPath);
            return null;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Checks if state file exists
    /// </summary>
    public bool StateExists()
    {
        return File.Exists(_statePath);
    }

    /// <summary>
    /// Deletes state file and backup
    /// </summary>
    public async Task DeleteStateAsync(CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            if (File.Exists(_statePath))
            {
                File.Delete(_statePath);
                _logger.Information("🗑️ State file deleted: {Path}", _statePath);
            }

            if (File.Exists(_backupPath))
            {
                File.Delete(_backupPath);
                _logger.Debug("Backup file deleted: {Path}", _backupPath);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete state files");
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Creates a manual backup of current state
    /// </summary>
    public async Task CreateBackupAsync(CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_statePath))
            {
                _logger.Warning("Cannot create backup - no state file exists");
                return;
            }

            File.Copy(_statePath, _backupPath, overwrite: true);
            _logger.Information("💾 Backup created: {Path}", _backupPath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create backup");
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public void Dispose()
    {
        _fileLock?.Dispose();
    }
}
