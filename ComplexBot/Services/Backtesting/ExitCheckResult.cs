namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Exit check result
/// </summary>
public record ExitCheckResult(
    bool ShouldExit,
    decimal ExitPrice,
    string Reason);
