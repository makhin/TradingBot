namespace ComplexBot.Services.Trading;

/// <summary>
/// Pairs a filter trader with its filter logic.
/// </summary>
/// <param name="Trader">Trader on alternative timeframe</param>
/// <param name="Filter">Filter that evaluates signals based on this trader's state</param>
public record FilterTraderPair(ISymbolTrader Trader, ISignalFilter Filter);
