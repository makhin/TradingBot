using TradingBot.Core.Models;
using TradingBot.Core.RiskManagement;

namespace ComplexBot.Services.Backtesting;

public abstract class StrategyOptimizerBase<TSettings, TConfig>
    where TSettings : class
{
    protected StrategyOptimizerBase(
        TConfig config,
        RiskSettings? riskSettings,
        BacktestSettings? backtestSettings,
        PerformanceFitnessPolicy? policy = null)
    {
        Config = config;
        Random = new Random();
        RiskSettings = riskSettings ?? new RiskSettings();
        BacktestSettings = backtestSettings ?? new BacktestSettings();
        Policy = policy ?? new PerformanceFitnessPolicy();
        FitnessCalculator = new PerformanceFitnessCalculator(Policy);
    }

    protected TConfig Config { get; }
    protected Random Random { get; }
    protected RiskSettings RiskSettings { get; }
    protected BacktestSettings BacktestSettings { get; }
    protected PerformanceFitnessPolicy Policy { get; }
    protected PerformanceFitnessCalculator FitnessCalculator { get; }

    public GeneticOptimizer<TSettings> CreateOptimizer(GeneticOptimizerSettings? settings = null)
    {
        return new GeneticOptimizer<TSettings>(
            createRandom: CreateRandom,
            mutate: Mutate,
            crossover: Crossover,
            validate: Validate,
            settings: settings
        );
    }

    public GeneticOptimizationResult<TSettings> Optimize(
        List<Candle> candles,
        string symbol,
        GeneticOptimizerSettings? settings = null,
        IProgress<GeneticProgress<TSettings>>? progress = null)
    {
        if (candles == null || candles.Count < 200)
            throw new ArgumentException("Insufficient data for optimization. Required: 200 candles minimum", nameof(candles));

        var optimizer = CreateOptimizer(settings);
        return optimizer.Optimize(candidate => EvaluateFitness(candidate, candles, symbol), progress);
    }

    protected abstract TSettings CreateRandom();

    protected abstract TSettings Mutate(TSettings settings);

    protected abstract TSettings Crossover(TSettings parent1, TSettings parent2);

    protected abstract bool Validate(TSettings settings);

    protected abstract decimal EvaluateFitness(TSettings settings, List<Candle> candles, string symbol);

    protected int RandomInt(int min, int max) => Random.Next(min, max + 1);

    protected decimal RandomDecimal(decimal min, decimal max)
    {
        return min + (decimal)Random.NextDouble() * (max - min);
    }

    protected int MutateInt(int value, int min, int max)
    {
        var delta = (max - min) / 4;
        var newValue = value + Random.Next(-delta, delta + 1);
        return Math.Clamp(newValue, min, max);
    }

    protected decimal MutateDecimal(decimal value, decimal min, decimal max)
    {
        var delta = (max - min) / 4;
        var newValue = value + (decimal)(Random.NextDouble() * 2 - 1) * delta;
        return Math.Clamp(newValue, min, max);
    }

    protected static bool IsInRange(decimal value, decimal min, decimal max) =>
        value >= min && value <= max;

    protected T Pick<T>(T a, T b) => Random.NextDouble() > 0.5 ? a : b;
}
