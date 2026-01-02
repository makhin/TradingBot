using ComplexBot.Models;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.RiskManagement;

namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Genetic Algorithm Parameter Optimizer
/// Uses evolutionary algorithms to find optimal strategy parameters.
/// More efficient than grid search for large parameter spaces.
/// </summary>
public class GeneticOptimizer
{
    private readonly GeneticOptimizerSettings _settings;
    private readonly Random _random;

    public GeneticOptimizer(GeneticOptimizerSettings? settings = null)
    {
        _settings = settings ?? new GeneticOptimizerSettings();
        _random = new Random(_settings.RandomSeed ?? Environment.TickCount);
    }

    public GeneticOptimizationResult Optimize(
        List<Candle> candles,
        string symbol,
        RiskSettings riskSettings,
        BacktestSettings backtestSettings,
        IProgress<GeneticProgress>? progress = null)
    {
        // Split data: 70% training, 30% validation
        int splitIndex = (int)(candles.Count * _settings.TrainingRatio);
        var trainingData = candles.Take(splitIndex).ToList();
        var validationData = candles.Skip(splitIndex).ToList();

        // Initialize population
        var population = InitializePopulation();
        var generationStats = new List<GenerationStats>();
        Chromosome? bestEver = null;

        for (int gen = 0; gen < _settings.Generations; gen++)
        {
            // Evaluate fitness for each chromosome
            EvaluatePopulation(population, trainingData, symbol, riskSettings, backtestSettings);

            // Track statistics
            var stats = CalculateGenerationStats(gen, population);
            generationStats.Add(stats);

            // Update best ever
            var currentBest = population.MaxBy(c => c.Fitness);
            if (currentBest != null && (bestEver == null || currentBest.Fitness > bestEver.Fitness))
            {
                bestEver = currentBest with { }; // Clone
            }

            // Report progress
            progress?.Report(new GeneticProgress(
                gen + 1,
                _settings.Generations,
                stats.BestFitness,
                stats.AverageFitness,
                currentBest?.Settings
            ));

            // Check for early stopping
            if (ShouldStop(generationStats))
            {
                progress?.Report(new GeneticProgress(
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

        // Validate best parameters on out-of-sample data
        var validationResults = ValidateBestParameters(
            population.OrderByDescending(c => c.Fitness).Take(10).ToList(),
            validationData,
            symbol,
            riskSettings,
            backtestSettings
        );

        return new GeneticOptimizationResult(
            bestEver?.Settings ?? new StrategySettings(),
            bestEver?.Fitness ?? 0,
            validationResults,
            generationStats,
            trainingData.First().OpenTime,
            trainingData.Last().CloseTime,
            validationData.First().OpenTime,
            validationData.Last().CloseTime
        );
    }

    private List<Chromosome> InitializePopulation()
    {
        var population = new List<Chromosome>();

        for (int i = 0; i < _settings.PopulationSize; i++)
        {
            population.Add(new Chromosome
            {
                Settings = GenerateRandomSettings()
            });
        }

        return population;
    }

    private StrategySettings GenerateRandomSettings()
    {
        return new StrategySettings
        {
            AdxPeriod = RandomInRange(_settings.AdxPeriodMin, _settings.AdxPeriodMax),
            AdxThreshold = RandomDecimalInRange(_settings.AdxThresholdMin, _settings.AdxThresholdMax),
            AdxExitThreshold = RandomDecimalInRange(_settings.AdxExitThresholdMin, _settings.AdxExitThresholdMax),
            FastEmaPeriod = RandomInRange(_settings.FastEmaMin, _settings.FastEmaMax),
            SlowEmaPeriod = RandomInRange(_settings.SlowEmaMin, _settings.SlowEmaMax),
            AtrPeriod = 14,
            AtrStopMultiplier = RandomDecimalInRange(_settings.AtrMultiplierMin, _settings.AtrMultiplierMax),
            TakeProfitMultiplier = RandomDecimalInRange(_settings.TakeProfitMultiplierMin, _settings.TakeProfitMultiplierMax),
            VolumeThreshold = RandomDecimalInRange(_settings.VolumeThresholdMin, _settings.VolumeThresholdMax),
            RequireVolumeConfirmation = _random.NextDouble() > 0.5,
            RequireObvConfirmation = _random.NextDouble() > 0.5
        };
    }

    private void EvaluatePopulation(
        List<Chromosome> population,
        List<Candle> data,
        string symbol,
        RiskSettings riskSettings,
        BacktestSettings backtestSettings)
    {
        // Parallel evaluation for better performance
        Parallel.ForEach(population, chromosome =>
        {
            if (!chromosome.IsEvaluated)
            {
                chromosome.Fitness = EvaluateFitness(
                    chromosome.Settings,
                    data,
                    symbol,
                    riskSettings,
                    backtestSettings
                );
                chromosome.IsEvaluated = true;
            }
        });
    }

    private decimal EvaluateFitness(
        StrategySettings settings,
        List<Candle> data,
        string symbol,
        RiskSettings riskSettings,
        BacktestSettings backtestSettings)
    {
        // Validate settings
        if (settings.FastEmaPeriod >= settings.SlowEmaPeriod)
            return -1000m;

        try
        {
            var strategy = new AdxTrendStrategy(settings);
            var engine = new BacktestEngine(strategy, riskSettings, backtestSettings);
            var result = engine.Run(data, symbol);

            // Require minimum trades
            if (result.Metrics.TotalTrades < _settings.MinTrades)
                return -100m;

            // Calculate fitness based on selected target
            return _settings.FitnessFunction switch
            {
                FitnessFunction.Sharpe => result.Metrics.SharpeRatio,
                FitnessFunction.Sortino => result.Metrics.SortinoRatio,
                FitnessFunction.ProfitFactor => result.Metrics.ProfitFactor,
                FitnessFunction.Return => result.Metrics.TotalReturn,
                FitnessFunction.RiskAdjusted => CalculateRiskAdjustedFitness(result.Metrics),
                FitnessFunction.Combined => CalculateCombinedFitness(result.Metrics),
                _ => result.Metrics.SharpeRatio
            };
        }
        catch
        {
            return -1000m;
        }
    }

    private decimal CalculateRiskAdjustedFitness(PerformanceMetrics metrics)
    {
        // Penalize high drawdowns
        var drawdownPenalty = Math.Max(0, metrics.MaxDrawdownPercent - 20) * 0.1m;
        var sharpe = metrics.SharpeRatio;
        return sharpe - drawdownPenalty;
    }

    private decimal CalculateCombinedFitness(PerformanceMetrics metrics)
    {
        // Multi-objective: Sharpe * (1 + ProfitFactor/10) * (1 - MaxDD/100)
        var sharpeComponent = Math.Max(0, metrics.SharpeRatio);
        var pfComponent = 1 + Math.Min(3, metrics.ProfitFactor) / 10;
        var ddComponent = 1 - Math.Min(1, metrics.MaxDrawdownPercent / 100);

        return sharpeComponent * pfComponent * ddComponent;
    }

    private List<Chromosome> EvolvePopulation(List<Chromosome> population)
    {
        var newPopulation = new List<Chromosome>();

        // Elitism: keep top performers
        var elite = population
            .OrderByDescending(c => c.Fitness)
            .Take(_settings.EliteCount)
            .Select(c => c with { IsEvaluated = true })
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

    private Chromosome SelectParent(List<Chromosome> population)
    {
        // Tournament selection
        var tournament = new List<Chromosome>();
        for (int i = 0; i < _settings.TournamentSize; i++)
        {
            tournament.Add(population[_random.Next(population.Count)]);
        }
        return tournament.MaxBy(c => c.Fitness) ?? population[0];
    }

    private Chromosome Crossover(Chromosome parent1, Chromosome parent2)
    {
        if (_random.NextDouble() > _settings.CrossoverRate)
        {
            return _random.NextDouble() > 0.5 ? parent1 with { IsEvaluated = false } : parent2 with { IsEvaluated = false };
        }

        // Uniform crossover
        return new Chromosome
        {
            Settings = new StrategySettings
            {
                AdxPeriod = Pick(parent1.Settings.AdxPeriod, parent2.Settings.AdxPeriod),
                AdxThreshold = Pick(parent1.Settings.AdxThreshold, parent2.Settings.AdxThreshold),
                AdxExitThreshold = Pick(parent1.Settings.AdxExitThreshold, parent2.Settings.AdxExitThreshold),
                FastEmaPeriod = Pick(parent1.Settings.FastEmaPeriod, parent2.Settings.FastEmaPeriod),
                SlowEmaPeriod = Pick(parent1.Settings.SlowEmaPeriod, parent2.Settings.SlowEmaPeriod),
                AtrPeriod = 14,
                AtrStopMultiplier = Pick(parent1.Settings.AtrStopMultiplier, parent2.Settings.AtrStopMultiplier),
                TakeProfitMultiplier = Pick(parent1.Settings.TakeProfitMultiplier, parent2.Settings.TakeProfitMultiplier),
                VolumeThreshold = Pick(parent1.Settings.VolumeThreshold, parent2.Settings.VolumeThreshold),
                RequireVolumeConfirmation = Pick(parent1.Settings.RequireVolumeConfirmation, parent2.Settings.RequireVolumeConfirmation),
                RequireObvConfirmation = Pick(parent1.Settings.RequireObvConfirmation, parent2.Settings.RequireObvConfirmation)
            },
            IsEvaluated = false
        };
    }

    private T Pick<T>(T a, T b) => _random.NextDouble() > 0.5 ? a : b;

    private Chromosome Mutate(Chromosome chromosome)
    {
        if (_random.NextDouble() > _settings.MutationRate)
            return chromosome;

        var settings = chromosome.Settings;

        // Mutate random parameter
        var paramIndex = _random.Next(8);
        settings = paramIndex switch
        {
            0 => settings with { AdxPeriod = MutateInt(settings.AdxPeriod, _settings.AdxPeriodMin, _settings.AdxPeriodMax) },
            1 => settings with { AdxThreshold = MutateDecimal(settings.AdxThreshold, _settings.AdxThresholdMin, _settings.AdxThresholdMax) },
            2 => settings with { FastEmaPeriod = MutateInt(settings.FastEmaPeriod, _settings.FastEmaMin, _settings.FastEmaMax) },
            3 => settings with { SlowEmaPeriod = MutateInt(settings.SlowEmaPeriod, _settings.SlowEmaMin, _settings.SlowEmaMax) },
            4 => settings with { AtrStopMultiplier = MutateDecimal(settings.AtrStopMultiplier, _settings.AtrMultiplierMin, _settings.AtrMultiplierMax) },
            5 => settings with { TakeProfitMultiplier = MutateDecimal(settings.TakeProfitMultiplier, _settings.TakeProfitMultiplierMin, _settings.TakeProfitMultiplierMax) },
            6 => settings with { VolumeThreshold = MutateDecimal(settings.VolumeThreshold, _settings.VolumeThresholdMin, _settings.VolumeThresholdMax) },
            _ => settings with { RequireVolumeConfirmation = !settings.RequireVolumeConfirmation }
        };

        return new Chromosome { Settings = settings, IsEvaluated = false };
    }

    private int MutateInt(int value, int min, int max)
    {
        var delta = (max - min) / 4;
        var newValue = value + _random.Next(-delta, delta + 1);
        return Math.Clamp(newValue, min, max);
    }

    private decimal MutateDecimal(decimal value, decimal min, decimal max)
    {
        var delta = (max - min) / 4;
        var newValue = value + (decimal)(_random.NextDouble() * 2 - 1) * delta;
        return Math.Clamp(newValue, min, max);
    }

    private int RandomInRange(int min, int max) => _random.Next(min, max + 1);

    private decimal RandomDecimalInRange(decimal min, decimal max)
    {
        return min + (decimal)_random.NextDouble() * (max - min);
    }

    private GenerationStats CalculateGenerationStats(int generation, List<Chromosome> population)
    {
        var fitnesses = population.Select(c => c.Fitness).ToList();
        return new GenerationStats(
            generation,
            fitnesses.Max(),
            fitnesses.Average(),
            fitnesses.Min(),
            population.MaxBy(c => c.Fitness)?.Settings ?? new StrategySettings()
        );
    }

    private bool ShouldStop(List<GenerationStats> stats)
    {
        if (stats.Count < _settings.EarlyStoppingPatience)
            return false;

        var recent = stats.TakeLast(_settings.EarlyStoppingPatience).ToList();
        var improvement = recent.Last().BestFitness - recent.First().BestFitness;

        return improvement < _settings.EarlyStoppingThreshold;
    }

    private List<ValidationResult> ValidateBestParameters(
        List<Chromosome> topChromosomes,
        List<Candle> validationData,
        string symbol,
        RiskSettings riskSettings,
        BacktestSettings backtestSettings)
    {
        var results = new List<ValidationResult>();

        foreach (var chromosome in topChromosomes)
        {
            var strategy = new AdxTrendStrategy(chromosome.Settings);
            var engine = new BacktestEngine(strategy, riskSettings, backtestSettings);
            var result = engine.Run(validationData, symbol);

            var validationFitness = _settings.FitnessFunction switch
            {
                FitnessFunction.Sharpe => result.Metrics.SharpeRatio,
                FitnessFunction.Sortino => result.Metrics.SortinoRatio,
                FitnessFunction.ProfitFactor => result.Metrics.ProfitFactor,
                FitnessFunction.Return => result.Metrics.TotalReturn,
                _ => result.Metrics.SharpeRatio
            };

            results.Add(new ValidationResult(
                chromosome.Settings,
                chromosome.Fitness,
                validationFitness,
                result
            ));
        }

        return results.OrderByDescending(r => r.ValidationFitness).ToList();
    }
}

public record Chromosome
{
    public StrategySettings Settings { get; init; } = new();
    public decimal Fitness { get; set; }
    public bool IsEvaluated { get; set; }
}

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

    // Fitness
    public FitnessFunction FitnessFunction { get; init; } = FitnessFunction.RiskAdjusted;
    public int MinTrades { get; init; } = 30;

    // Data split
    public decimal TrainingRatio { get; init; } = 0.7m;

    // Random seed (null = random)
    public int? RandomSeed { get; init; }

    // Parameter ranges
    public int AdxPeriodMin { get; init; } = 10;
    public int AdxPeriodMax { get; init; } = 25;
    public decimal AdxThresholdMin { get; init; } = 18m;
    public decimal AdxThresholdMax { get; init; } = 35m;
    public decimal AdxExitThresholdMin { get; init; } = 12m;
    public decimal AdxExitThresholdMax { get; init; } = 25m;
    public int FastEmaMin { get; init; } = 8;
    public int FastEmaMax { get; init; } = 30;
    public int SlowEmaMin { get; init; } = 35;
    public int SlowEmaMax { get; init; } = 100;
    public decimal AtrMultiplierMin { get; init; } = 1.5m;
    public decimal AtrMultiplierMax { get; init; } = 4.0m;
    public decimal TakeProfitMultiplierMin { get; init; } = 1.0m;
    public decimal TakeProfitMultiplierMax { get; init; } = 3.0m;
    public decimal VolumeThresholdMin { get; init; } = 1.0m;
    public decimal VolumeThresholdMax { get; init; } = 2.5m;
}

public enum FitnessFunction
{
    Sharpe,
    Sortino,
    ProfitFactor,
    Return,
    RiskAdjusted,
    Combined
}

public record GenerationStats(
    int Generation,
    decimal BestFitness,
    decimal AverageFitness,
    decimal WorstFitness,
    StrategySettings BestSettings
);

public record ValidationResult(
    StrategySettings Settings,
    decimal TrainingFitness,
    decimal ValidationFitness,
    BacktestResult BacktestResult
)
{
    public decimal Robustness => TrainingFitness > 0 ? ValidationFitness / TrainingFitness * 100 : 0;
    public bool IsRobust => Robustness >= 50;
}

public record GeneticOptimizationResult(
    StrategySettings BestSettings,
    decimal BestFitness,
    List<ValidationResult> ValidationResults,
    List<GenerationStats> GenerationHistory,
    DateTime TrainingStart,
    DateTime TrainingEnd,
    DateTime ValidationStart,
    DateTime ValidationEnd
)
{
    public ValidationResult? BestRobustResult => ValidationResults.FirstOrDefault(r => r.IsRobust);
    public decimal ConvergenceRate => GenerationHistory.Count > 1
        ? (GenerationHistory.Last().BestFitness - GenerationHistory.First().BestFitness) / GenerationHistory.Count
        : 0;
}

public record GeneticProgress(
    int CurrentGeneration,
    int TotalGenerations,
    decimal BestFitness,
    decimal AverageFitness,
    StrategySettings? CurrentBest,
    string? Message = null
)
{
    public int PercentComplete => TotalGenerations > 0 ? CurrentGeneration * 100 / TotalGenerations : 0;
}
