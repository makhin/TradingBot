using System.Collections.Generic;
using System.Linq;

namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Final optimization result
/// </summary>
public record GeneticOptimizationResult<TSettings>(
    TSettings BestSettings,
    decimal BestFitness,
    List<GenerationStats<TSettings>> GenerationHistory
) where TSettings : class
{
    public decimal ConvergenceRate => GenerationHistory.Count > 1
        ? (GenerationHistory.Last().BestFitness - GenerationHistory.First().BestFitness) / GenerationHistory.Count
        : 0;
}
