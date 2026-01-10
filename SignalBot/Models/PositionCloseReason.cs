namespace SignalBot.Models;

/// <summary>
/// Reason for position closure
/// </summary>
public enum PositionCloseReason
{
    AllTargetsHit,
    StopLossHit,
    ManualClose,
    Liquidation,
    Error
}
