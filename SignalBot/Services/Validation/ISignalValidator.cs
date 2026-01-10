using SignalBot.Models;

namespace SignalBot.Services.Validation;

/// <summary>
/// Interface for signal validation and adjustment
/// </summary>
public interface ISignalValidator
{
    Task<ValidationResult> ValidateAndAdjustAsync(
        TradingSignal signal,
        decimal accountEquity,
        CancellationToken ct = default);
}
