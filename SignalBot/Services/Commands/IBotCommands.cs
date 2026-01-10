namespace SignalBot.Services.Commands;

/// <summary>
/// Interface for bot control commands (Telegram/CLI)
/// </summary>
public interface IBotCommands
{
    /// <summary>
    /// Get current bot status (mode, positions, cooldown, etc.)
    /// </summary>
    Task<string> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Get list of open positions
    /// </summary>
    Task<string> GetPositionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Pause trading (stop accepting new signals, keep positions)
    /// </summary>
    Task<string> PauseAsync(CancellationToken ct = default);

    /// <summary>
    /// Resume trading (accept new signals again)
    /// </summary>
    Task<string> ResumeAsync(CancellationToken ct = default);

    /// <summary>
    /// Close all open positions at market price
    /// </summary>
    Task<string> CloseAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Close specific position by symbol
    /// </summary>
    Task<string> ClosePositionAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// Emergency stop - close all positions and stop bot
    /// </summary>
    Task<string> EmergencyStopAsync(CancellationToken ct = default);

    /// <summary>
    /// Reset cooldown period (allow immediate trading)
    /// </summary>
    Task<string> ResetCooldownAsync(CancellationToken ct = default);

    /// <summary>
    /// Get help message with available commands
    /// </summary>
    string GetHelp();
}
