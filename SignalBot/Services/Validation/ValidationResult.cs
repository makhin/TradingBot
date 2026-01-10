using SignalBot.Models;

namespace SignalBot.Services.Validation;

/// <summary>
/// Result of signal validation
/// </summary>
public record ValidationResult
{
    public bool IsSuccess { get; init; }
    public TradingSignal? ValidatedSignal { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public static ValidationResult Success(TradingSignal signal) => new()
    {
        IsSuccess = true,
        ValidatedSignal = signal,
        Warnings = signal.ValidationWarnings
    };

    public static ValidationResult Failed(string errorMessage) => new()
    {
        IsSuccess = false,
        ErrorMessage = errorMessage
    };
}
