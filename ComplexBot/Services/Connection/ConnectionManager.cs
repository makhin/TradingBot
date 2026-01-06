using Binance.Net.Clients;
using Binance.Net.Interfaces;
using Binance.Net.Objects.Models.Spot.Socket;
using CryptoExchange.Net.Objects.Sockets;
using CryptoExchange.Net.Sockets;
using Serilog;
using Serilog.Events;
using ComplexBot.Configuration;
using ComplexBot.Models;
using ComplexBot.Services.Backtesting;

namespace ComplexBot.Services.Connection;

public class ConnectionManager
{
    private readonly BinanceSocketClient _socketClient;
    private readonly ConnectionSettings _connectionSettings;
    private readonly int[] _backoffDelays;
    private int _reconnectAttempt = 0;
    private bool _isConnected = false;
    private UpdateSubscription? _currentSubscription;
    private readonly CancellationTokenSource _healthCheckCts = new();
    private readonly ILogger _logger = Serilog.Log.ForContext<ConnectionManager>();
    private DateTimeOffset? _lastConnectedAt;
    private DateTimeOffset? _lastDisconnectedAt;
    private DateTimeOffset? _lastErrorAt;
    private string? _lastErrorMessage;

    public event Action? OnConnected;
    public event Action<string>? OnDisconnected;
    public event Action<Exception>? OnError;
    public event Action<string>? OnLog;
    public event Action? OnCriticalFailure;

    public bool IsConnected => _isConnected;
    public int ReconnectAttempt => _reconnectAttempt;

    public ConnectionManager(BinanceSocketClient socketClient, ConnectionSettings? connectionSettings = null)
    {
        _socketClient = socketClient;
        _connectionSettings = connectionSettings ?? new ConnectionSettings();
        _backoffDelays = BuildBackoffDelays(_connectionSettings.Backoff);
    }

    public async Task<bool> ConnectWithRetry(
        string symbol,
        KlineInterval interval,
        Action<DataEvent<IBinanceStreamKlineData>> onKline,
        CancellationToken cancellationToken = default)
    {
        while (_reconnectAttempt < _backoffDelays.Length && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                Log($"🔌 Connecting to {symbol} {interval} stream (attempt {_reconnectAttempt + 1}/{_backoffDelays.Length})...", LogEventLevel.Information);

                var result = await _socketClient.SpotApi.ExchangeData
                    .SubscribeToKlineUpdatesAsync(
                        symbol,
                        interval.ToBinanceInterval(),
                        onKline
                    );

                if (result.Success)
                {
                    _currentSubscription = result.Data;
                    _isConnected = true;
                    _reconnectAttempt = 0;
                    _lastConnectedAt = DateTimeOffset.UtcNow;
                    Log($"✅ Connected to {symbol} {interval} stream", LogEventLevel.Information);
                    OnConnected?.Invoke();

                    // Set up connection loss handler
                    result.Data.ConnectionLost += () =>
                    {
                        _isConnected = false;
                        _lastDisconnectedAt = DateTimeOffset.UtcNow;
                        Log("⚠️ WebSocket connection lost", LogEventLevel.Warning);
                        OnDisconnected?.Invoke("Connection lost");
                        _ = ReconnectAsync(symbol, interval, onKline, CancellationToken.None);
                    };

                    // Set up connection restored handler
                    result.Data.ConnectionRestored += (time) =>
                    {
                        _isConnected = true;
                        _lastConnectedAt = DateTimeOffset.UtcNow;
                        Log($"✅ WebSocket connection restored (downtime: {time.TotalSeconds:F1}s)", LogEventLevel.Information);
                        OnConnected?.Invoke();
                    };

                    return true;
                }
                else
                {
                    throw new Exception(result.Error?.Message ?? "Unknown error");
                }
            }
            catch (OperationCanceledException)
            {
                Log("🛑 Connection retry cancelled", LogEventLevel.Information);
                return false;
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
                _lastErrorMessage = ex.Message;
                _lastErrorAt = DateTimeOffset.UtcNow;
                var delay = GetJitteredDelay(_backoffDelays[_reconnectAttempt]);
                Log($"❌ Connection failed: {ex.Message}", LogEventLevel.Error);
                Log($"⏳ Retrying in {delay}ms... (attempt {_reconnectAttempt + 1}/{_backoffDelays.Length})", LogEventLevel.Warning);

                await Task.Delay(delay, cancellationToken);
                _reconnectAttempt++;
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            Log("🛑 Connection retry cancelled", LogEventLevel.Information);
            return false;
        }

        Log($"❌ Max reconnection attempts reached ({_backoffDelays.Length})", LogEventLevel.Error);
        Log("⚠️ Bot will stop - manual intervention required", LogEventLevel.Warning);
        OnCriticalFailure?.Invoke();
        return false;
    }

    private async Task ReconnectAsync(
        string symbol,
        KlineInterval interval,
        Action<DataEvent<IBinanceStreamKlineData>> onKline,
        CancellationToken cancellationToken = default)
    {
        Log("🔄 Starting automatic reconnection...", LogEventLevel.Information);
        var success = await ConnectWithRetry(symbol, interval, onKline, cancellationToken);

        if (!success)
        {
            Log("❌ Automatic reconnection failed", LogEventLevel.Error);
            Log("💡 Recommendation: Check internet connection and restart bot", LogEventLevel.Warning);
            OnCriticalFailure?.Invoke();
        }
    }

    public async Task StartHealthCheck(TimeSpan interval, CancellationToken cancellationToken = default)
    {
        Log($"💚 Starting health check (interval: {interval.TotalSeconds}s)", LogEventLevel.Information);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(interval, cancellationToken);

                if (!_isConnected)
                {
                    Log("💔 Health check: DISCONNECTED", LogEventLevel.Warning);
                }
                else
                {
                    Log("💚 Health check: Connected", LogEventLevel.Information);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log("🛑 Health check stopped", LogEventLevel.Information);
        }
    }

    public async Task DisconnectAsync()
    {
        if (_currentSubscription != null)
        {
            await _currentSubscription.CloseAsync();
            _currentSubscription = null;
            _isConnected = false;
            _lastDisconnectedAt = DateTimeOffset.UtcNow;
            Log("🔌 Disconnected from stream", LogEventLevel.Information);
        }

        _healthCheckCts.Cancel();
    }

    public void ResetReconnectAttempts()
    {
        _reconnectAttempt = 0;
        Log("🔄 Reconnect attempts reset", LogEventLevel.Information);
    }

    private void Log(string message)
    {
        Log(message, LogEventLevel.Information);
    }

    private void Log(string message, LogEventLevel level)
    {
        _logger.Write(level, "[ConnectionManager] {Message}", message);
        OnLog?.Invoke(message);
    }

    public ConnectionStats GetStats()
    {
        return new ConnectionStats(
            _isConnected,
            _reconnectAttempt,
            _backoffDelays.Length,
            _currentSubscription != null,
            _lastConnectedAt,
            _lastDisconnectedAt,
            _lastErrorMessage,
            _lastErrorAt
        );
    }

    private int GetJitteredDelay(int baseDelayMs)
    {
        var jitterFactor = Math.Clamp(_connectionSettings.JitterFactor, 0.0, 1.0);
        if (jitterFactor <= 0)
        {
            return baseDelayMs;
        }

        var jitterMultiplier = 1 + ((Random.Shared.NextDouble() * 2) - 1) * jitterFactor;
        var jitteredDelay = (int)Math.Round(baseDelayMs * jitterMultiplier);
        return Math.Max(0, jitteredDelay);
    }

    private static int[] BuildBackoffDelays(BackoffSettings settings)
    {
        if (settings.DelaysMs is { Count: > 0 })
        {
            return settings.DelaysMs.ToArray();
        }

        var delays = new List<int>();
        var currentDelay = Math.Max(1, settings.MinDelayMs);

        while (currentDelay < settings.MaxDelayMs)
        {
            delays.Add(currentDelay);
            var nextDelay = (int)Math.Round(currentDelay * settings.Factor);
            if (nextDelay <= currentDelay)
            {
                nextDelay = currentDelay + 1;
            }
            currentDelay = Math.Min(nextDelay, settings.MaxDelayMs);
        }

        delays.Add(settings.MaxDelayMs);
        return delays.ToArray();
    }
}
