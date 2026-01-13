using Serilog;

namespace SignalBot.Services;

/// <summary>
/// Base class for services with start/stop lifecycle
/// </summary>
public abstract class ServiceBase : IAsyncDisposable
{
    protected readonly ILogger _logger;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private bool _isRunning;
    private bool _disposed;

    /// <summary>
    /// Indicates whether the service is currently running
    /// </summary>
    public bool IsRunning => _isRunning;

    protected ServiceBase(ILogger? logger = null)
    {
        _logger = logger ?? Log.ForContext(GetType());
    }

    /// <summary>
    /// Starts the service
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            if (_isRunning)
            {
                _logger.Warning("{ServiceName} is already running", GetServiceName());
                return;
            }

            _logger.Information("Starting {ServiceName}...", GetServiceName());

            try
            {
                await OnStartAsync(ct);
                _isRunning = true;
                _logger.Information("{ServiceName} started successfully", GetServiceName());
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to start {ServiceName}", GetServiceName());
                throw;
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Stops the service
    /// </summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            if (!_isRunning)
            {
                _logger.Warning("{ServiceName} is not running", GetServiceName());
                return;
            }

            _logger.Information("Stopping {ServiceName}...", GetServiceName());

            await OnStopAsync(ct);

            _isRunning = false;
            _logger.Information("{ServiceName} stopped", GetServiceName());
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Gets the service name for logging. Override to customize.
    /// </summary>
    protected virtual string GetServiceName() => GetType().Name;

    /// <summary>
    /// Called when the service should start. Implement startup logic here.
    /// </summary>
    protected abstract Task OnStartAsync(CancellationToken ct);

    /// <summary>
    /// Called when the service should stop. Implement shutdown logic here.
    /// </summary>
    protected abstract Task OnStopAsync(CancellationToken ct);

    /// <summary>
    /// Disposes the service and stops it if running
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await StopAsync();
        await OnDisposeAsync();
        _stateLock.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Called during disposal for additional cleanup. Override if needed.
    /// </summary>
    protected virtual ValueTask OnDisposeAsync() => ValueTask.CompletedTask;
}
