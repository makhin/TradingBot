using SignalBot.Models;

namespace SignalBot.Services.Trading;

/// <summary>
/// Interface for executing trading signals
/// </summary>
public interface ISignalTrader
{
    Task<SignalPosition> ExecuteSignalAsync(
        TradingSignal signal,
        decimal accountEquity,
        CancellationToken ct = default);
}
