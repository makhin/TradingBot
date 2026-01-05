namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Statistics for a single generation
/// </summary>
public record GenerationStats<TSettings>(
    int Generation,
    decimal BestFitness,
    decimal AverageFitness,
    decimal WorstFitness,
    TSettings BestSettings
) where TSettings : class;
