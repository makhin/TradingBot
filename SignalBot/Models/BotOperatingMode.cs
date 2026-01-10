namespace SignalBot.Models;

/// <summary>
/// Operating mode of the trading bot
/// </summary>
public enum BotOperatingMode
{
    /// <summary>
    /// Fully automatic mode - accepts new signals and manages existing positions
    /// </summary>
    Automatic,

    /// <summary>
    /// Monitor only mode - ignores new signals but continues managing existing positions
    /// </summary>
    MonitorOnly,

    /// <summary>
    /// Paused - no new signals, no position management (positions remain open)
    /// </summary>
    Paused,

    /// <summary>
    /// Emergency stop - close all positions immediately
    /// </summary>
    EmergencyStop
}
