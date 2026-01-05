namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Genetic optimizer configuration (strategy-agnostic)
/// </summary>
public record GeneticOptimizerSettings
{
    // Population
    public int PopulationSize { get; init; } = 100;
    public int Generations { get; init; } = 50;
    public int EliteCount { get; init; } = 5;

    // Selection
    public int TournamentSize { get; init; } = 5;

    // Genetic operators
    public double CrossoverRate { get; init; } = 0.8;
    public double MutationRate { get; init; } = 0.15;

    // Early stopping
    public int EarlyStoppingPatience { get; init; } = 10;
    public decimal EarlyStoppingThreshold { get; init; } = 0.01m;

    // Random seed (null = random)
    public int? RandomSeed { get; init; }
}
