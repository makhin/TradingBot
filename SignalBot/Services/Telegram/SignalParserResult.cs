using SignalBot.Models;

namespace SignalBot.Services.Telegram;

/// <summary>
/// Result of signal parsing
/// </summary>
public record SignalParserResult
{
    public bool IsSuccess { get; init; }
    public TradingSignal? Signal { get; init; }
    public string? ErrorMessage { get; init; }

    public static SignalParserResult Success(TradingSignal signal) => new()
    {
        IsSuccess = true,
        Signal = signal
    };

    public static SignalParserResult Failed(string errorMessage) => new()
    {
        IsSuccess = false,
        ErrorMessage = errorMessage
    };
}
