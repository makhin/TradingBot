using Binance.Net.Enums;
using ComplexBot.Services.Strategies;

namespace ComplexBot.Services.Trading;

/// <summary>
/// Pairs a filter strategy with its interval and filter logic.
/// </summary>
/// <param name="Strategy">Filter strategy instance</param>
/// <param name="Interval">Interval the strategy runs on</param>
/// <param name="Filter">Filter that evaluates signals based on strategy state</param>
public record FilterStrategyPair(IStrategy Strategy, KlineInterval Interval, ISignalFilter Filter);
