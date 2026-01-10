namespace SignalBot.Models;

/// <summary>
/// Status of a signal position
/// </summary>
public enum PositionStatus
{
    Pending,         // Signal received, awaiting processing
    Opening,         // Entry order submitted
    Open,            // Position opened
    PartialClosed,   // Part of position closed by targets
    Closing,         // Closing in progress
    Closed,          // Fully closed
    Cancelled,       // Cancelled before opening
    Failed           // Error during opening
}
