namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Progress report during optimization
/// </summary>
public record GeneticProgress<TSettings>(
    int CurrentGeneration,
    int TotalGenerations,
    decimal BestFitness,
    decimal AverageFitness,
    TSettings? CurrentBest,
    string? Message = null
) where TSettings : class
{
    public int PercentComplete => TotalGenerations > 0 ? CurrentGeneration * 100 / TotalGenerations : 0;
}
