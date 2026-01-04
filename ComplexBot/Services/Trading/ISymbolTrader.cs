using ComplexBot.Models;

namespace ComplexBot.Services.Trading;

/// <summary>
/// Interface for trading a single symbol. Enables multi-exchange support.
/// </summary>
public interface ISymbolTrader : IDisposable
{
    // Identity
    string Symbol { get; }
    string Exchange { get; }  // "Binance", "Bybit", etc.

    // State
    decimal CurrentPosition { get; }
    decimal? EntryPrice { get; }
    decimal CurrentEquity { get; }
    bool IsRunning { get; }
    StrategyState GetStrategyState();

    // Lifecycle
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();

    // Position management
    Task ClosePositionAsync(string reason);

    // Events
    event Action<string>? OnLog;
    event Action<TradeSignal>? OnSignal;
    event Action<Trade>? OnTrade;
    event Action<decimal>? OnEquityUpdate;
}
