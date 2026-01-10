using SignalBot.Models;

namespace SignalBot.Services.Telegram;

/// <summary>
/// Interface for listening to Telegram signals
/// </summary>
public interface ITelegramSignalListener
{
    /// <summary>
    /// Starts listening to configured Telegram channels
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops listening
    /// </summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Event raised when a new signal is received and parsed
    /// </summary>
    event Action<TradingSignal>? OnSignalReceived;

    /// <summary>
    /// Whether the listener is currently active
    /// </summary>
    bool IsListening { get; }
}
