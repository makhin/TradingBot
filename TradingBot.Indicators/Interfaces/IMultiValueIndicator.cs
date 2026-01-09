using System.Collections.Generic;
using TradingBot.Core.Models;

namespace TradingBot.Indicators;

/// <summary>
/// Interface for indicators that produce multiple output values
/// </summary>
public interface IMultiValueIndicator : IIndicator
{
    /// <summary>
    /// Gets all output values as a dictionary (key = indicator name)
    /// </summary>
    IReadOnlyDictionary<string, decimal?> Values { get; }
}
