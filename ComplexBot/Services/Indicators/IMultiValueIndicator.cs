using System.Collections.Generic;

namespace ComplexBot.Services.Indicators;

/// <summary>
/// Interface for indicators that produce multiple output values
/// </summary>
public interface IMultiValueIndicator : IIndicator
{
    /// <summary>
    /// Gets all output values as a dictionary
    /// </summary>
    IReadOnlyDictionary<string, decimal?> Values { get; }
}
