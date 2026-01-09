using TradingBot.Core.Models;

namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Generic Genetic Algorithm Optimizer
/// Uses evolutionary algorithms to find optimal parameters for any strategy.
/// More efficient than grid search for large parameter spaces.
/// </summary>
/// <typeparam name="TSettings">Type of settings to optimize (e.g., StrategySettings, MaStrategySettings)</typeparam>
public class GeneticOptimizer<TSettings> where TSettings : class
{
    private readonly GeneticOptimizerSettings _settings;
    private readonly Random _random;
    private readonly Func<TSettings> _createRandom;
    private readonly Func<TSettings, TSettings> _mutate;
    private readonly Func<TSettings, TSettings, TSettings> _crossover;
    private readonly Func<TSettings, bool> _validate;

    /// <summary>
    /// Creates a genetic optimizer with custom delegates for strategy-specific operations
    /// </summary>
    /// <param name="createRandom">Delegate to create random settings within valid ranges</param>
    /// <param name="mutate">Delegate to mutate settings (returns mutated copy)</param>
    /// <param name="crossover">Delegate to crossover two parent settings (returns offspring)</param>
    /// <param name="validate">Delegate to validate settings (returns true if valid)</param>
    /// <param name="settings">Optimizer configuration</param>
    public GeneticOptimizer(
        Func<TSettings> createRandom,
        Func<TSettings, TSettings> mutate,
        Func<TSettings, TSettings, TSettings> crossover,
        Func<TSettings, bool> validate,
        GeneticOptimizerSettings? settings = null)
    {
        _createRandom = createRandom ?? throw new ArgumentNullException(nameof(createRandom));
        _mutate = mutate ?? throw new ArgumentNullException(nameof(mutate));
        _crossover = crossover ?? throw new ArgumentNullException(nameof(crossover));
        _validate = validate ?? throw new ArgumentNullException(nameof(validate));
        _settings = settings ?? new GeneticOptimizerSettings();
        _random = new Random(_settings.RandomSeed ?? Environment.TickCount);
    }

    /// <summary>
    /// Runs genetic optimization on the provided dataset
    /// </summary>
    /// <param name="evaluateFitness">Function that evaluates fitness for a given settings configuration</param>
    /// <param name="progress">Optional progress reporter</param>
    public GeneticOptimizationResult<TSettings> Optimize(
        Func<TSettings, decimal> evaluateFitness,
        IProgress<GeneticProgress<TSettings>>? progress = null)
    {
        // Initialize population
        var population = InitializePopulation();
        var generationStats = new List<GenerationStats<TSettings>>();
        Chromosome<TSettings>? bestEver = null;

        for (int gen = 0; gen < _settings.Generations; gen++)
        {
            // Evaluate fitness for each chromosome
            EvaluatePopulation(population, evaluateFitness);

            // Track statistics
            var stats = CalculateGenerationStats(gen, population);
            generationStats.Add(stats);

            // Update best ever
            var currentBest = population.MaxBy(c => c.Fitness);
            if (currentBest != null && (bestEver == null || currentBest.Fitness > bestEver.Fitness))
            {
                bestEver = new Chromosome<TSettings>
                {
                    Settings = currentBest.Settings,
                    Fitness = currentBest.Fitness,
                    IsEvaluated = true
                };
            }

            // Report progress
            progress?.Report(new GeneticProgress<TSettings>(
                gen + 1,
                _settings.Generations,
                stats.BestFitness,
                stats.AverageFitness,
                currentBest?.Settings
            ));

            // Check for early stopping
            if (ShouldStop(generationStats))
            {
                progress?.Report(new GeneticProgress<TSettings>(
                    gen + 1,
                    _settings.Generations,
                    stats.BestFitness,
                    stats.AverageFitness,
                    currentBest?.Settings,
                    "Early stopping: fitness plateaued"
                ));
                break;
            }

            // Don't evolve on last generation
            if (gen < _settings.Generations - 1)
            {
                population = EvolvePopulation(population);
            }
        }

        if (bestEver == null)
            throw new InvalidOperationException("Optimization failed - no valid solution found");

        return new GeneticOptimizationResult<TSettings>(
            bestEver.Settings,
            bestEver.Fitness,
            generationStats
        );
    }

    private List<Chromosome<TSettings>> InitializePopulation()
    {
        var population = new List<Chromosome<TSettings>>();

        for (int i = 0; i < _settings.PopulationSize; i++)
        {
            population.Add(new Chromosome<TSettings>
            {
                Settings = _createRandom()
            });
        }

        return population;
    }

    private void EvaluatePopulation(
        List<Chromosome<TSettings>> population,
        Func<TSettings, decimal> evaluateFitness)
    {
        // Parallel evaluation for better performance
        Parallel.ForEach(population, chromosome =>
        {
            if (!chromosome.IsEvaluated)
            {
                try
                {
                    chromosome.Fitness = evaluateFitness(chromosome.Settings);
                    chromosome.IsEvaluated = true;
                }
                catch
                {
                    chromosome.Fitness = -1000m;
                    chromosome.IsEvaluated = true;
                }
            }
        });
    }

    private List<Chromosome<TSettings>> EvolvePopulation(List<Chromosome<TSettings>> population)
    {
        var newPopulation = new List<Chromosome<TSettings>>();

        // Elitism: keep top performers
        var elite = population
            .OrderByDescending(c => c.Fitness)
            .Take(_settings.EliteCount)
            .Select(c => new Chromosome<TSettings>
            {
                Settings = c.Settings,
                Fitness = c.Fitness,
                IsEvaluated = true
            })
            .ToList();

        newPopulation.AddRange(elite);

        // Fill rest with offspring
        while (newPopulation.Count < _settings.PopulationSize)
        {
            var parent1 = SelectParent(population);
            var parent2 = SelectParent(population);

            var offspring = Crossover(parent1, parent2);
            offspring = Mutate(offspring);

            newPopulation.Add(offspring);
        }

        return newPopulation;
    }

    private Chromosome<TSettings> SelectParent(List<Chromosome<TSettings>> population)
    {
        // Tournament selection
        var tournament = new List<Chromosome<TSettings>>();
        for (int i = 0; i < _settings.TournamentSize; i++)
        {
            tournament.Add(population[_random.Next(population.Count)]);
        }
        return tournament.MaxBy(c => c.Fitness) ?? population[0];
    }

    private Chromosome<TSettings> Crossover(Chromosome<TSettings> parent1, Chromosome<TSettings> parent2)
    {
        if (_random.NextDouble() > _settings.CrossoverRate)
        {
            // No crossover - return copy of one parent
            return new Chromosome<TSettings>
            {
                Settings = _random.NextDouble() > 0.5 ? parent1.Settings : parent2.Settings,
                IsEvaluated = false
            };
        }

        // Apply custom crossover delegate
        return new Chromosome<TSettings>
        {
            Settings = _crossover(parent1.Settings, parent2.Settings),
            IsEvaluated = false
        };
    }

    private Chromosome<TSettings> Mutate(Chromosome<TSettings> chromosome)
    {
        if (_random.NextDouble() > _settings.MutationRate)
            return chromosome;

        // Apply custom mutation delegate
        return new Chromosome<TSettings>
        {
            Settings = _mutate(chromosome.Settings),
            IsEvaluated = false
        };
    }

    private GenerationStats<TSettings> CalculateGenerationStats(int generation, List<Chromosome<TSettings>> population)
    {
        var fitnesses = population.Select(c => c.Fitness).ToList();
        return new GenerationStats<TSettings>(
            generation,
            fitnesses.Max(),
            fitnesses.Average(),
            fitnesses.Min(),
            population.MaxBy(c => c.Fitness)?.Settings!
        );
    }

    private bool ShouldStop(List<GenerationStats<TSettings>> stats)
    {
        if (stats.Count < _settings.EarlyStoppingPatience)
            return false;

        var recent = stats.TakeLast(_settings.EarlyStoppingPatience).ToList();
        var improvement = recent.Last().BestFitness - recent.First().BestFitness;

        return improvement < _settings.EarlyStoppingThreshold;
    }
}
