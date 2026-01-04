using ComplexBot.Services.Backtesting;

namespace ComplexBot.Configuration.Optimization;

public class GeneticOptimizerConfigSettings
{
    public int PopulationSize { get; set; } = 100;
    public int Generations { get; set; } = 50;
    public int EliteCount { get; set; } = 5;
    public int TournamentSize { get; set; } = 5;
    public double CrossoverRate { get; set; } = 0.8;
    public double MutationRate { get; set; } = 0.15;
    public int EarlyStoppingPatience { get; set; } = 10;
    public decimal EarlyStoppingThreshold { get; set; } = 0.01m;
    public int? RandomSeed { get; set; }

    public GeneticOptimizerSettings ToGeneticOptimizerSettings() => new()
    {
        PopulationSize = PopulationSize,
        Generations = Generations,
        EliteCount = EliteCount,
        TournamentSize = TournamentSize,
        CrossoverRate = CrossoverRate,
        MutationRate = MutationRate,
        EarlyStoppingPatience = EarlyStoppingPatience,
        EarlyStoppingThreshold = EarlyStoppingThreshold,
        RandomSeed = RandomSeed
    };
}
