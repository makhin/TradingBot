using ComplexBot.Models;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.RiskManagement;

namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Optimizer configuration specifically for ADX Trend Following strategy
/// Provides parameter ranges and genetic operators for StrategySettings
/// </summary>
public class AdxStrategyOptimizer
{
    private readonly AdxOptimizerConfig _config;
    private readonly Random _random;
    private readonly RiskSettings _riskSettings;
    private readonly BacktestSettings _backtestSettings;
    private readonly FitnessFunction _fitnessFunction;
    private readonly int _minTrades;

    public AdxStrategyOptimizer(
        AdxOptimizerConfig? config = null,
        RiskSettings? riskSettings = null,
        BacktestSettings? backtestSettings = null,
        FitnessFunction fitnessFunction = FitnessFunction.RiskAdjusted,
        int minTrades = 30)
    {
        _config = config ?? new AdxOptimizerConfig();
        _random = new Random();
        _riskSettings = riskSettings ?? new RiskSettings();
        _backtestSettings = backtestSettings ?? new BacktestSettings();
        _fitnessFunction = fitnessFunction;
        _minTrades = minTrades;
    }

    /// <summary>
    /// Creates a genetic optimizer for ADX strategy
    /// </summary>
    public GeneticOptimizer<StrategySettings> CreateOptimizer(GeneticOptimizerSettings? settings = null)
    {
        return new GeneticOptimizer<StrategySettings>(
            createRandom: CreateRandom,
            mutate: Mutate,
            crossover: Crossover,
            validate: Validate,
            settings: settings
        );
    }

    /// <summary>
    /// Runs optimization on provided candle data
    /// </summary>
    public GeneticOptimizationResult<StrategySettings> Optimize(
        List<Candle> candles,
        string symbol,
        GeneticOptimizerSettings? settings = null,
        IProgress<GeneticProgress<StrategySettings>>? progress = null)
    {
        if (candles == null || candles.Count < 200)
            throw new ArgumentException("Insufficient data for optimization. Required: 200 candles minimum", nameof(candles));

        var optimizer = CreateOptimizer(settings);

        // Create fitness evaluator that captures backtesting context
        Func<StrategySettings, decimal> evaluateFitness = settings =>
            EvaluateFitness(settings, candles, symbol);

        return optimizer.Optimize(evaluateFitness, progress);
    }

    /// <summary>
    /// Creates random settings within configured ranges
    /// </summary>
    private StrategySettings CreateRandom()
    {
        return new StrategySettings
        {
            AdxPeriod = RandomInt(_config.AdxPeriodMin, _config.AdxPeriodMax),
            AdxThreshold = RandomDecimal(_config.AdxThresholdMin, _config.AdxThresholdMax),
            AdxExitThreshold = RandomDecimal(_config.AdxExitThresholdMin, _config.AdxExitThresholdMax),
            FastEmaPeriod = RandomInt(_config.FastEmaMin, _config.FastEmaMax),
            SlowEmaPeriod = RandomInt(_config.SlowEmaMin, _config.SlowEmaMax),
            AtrPeriod = 14,
            AtrStopMultiplier = RandomDecimal(_config.AtrMultiplierMin, _config.AtrMultiplierMax),
            TakeProfitMultiplier = RandomDecimal(_config.TakeProfitMultiplierMin, _config.TakeProfitMultiplierMax),
            VolumeThreshold = RandomDecimal(_config.VolumeThresholdMin, _config.VolumeThresholdMax),
            RequireVolumeConfirmation = _random.NextDouble() > 0.5,
            RequireObvConfirmation = _random.NextDouble() > 0.5
        };
    }

    /// <summary>
    /// Mutates settings by randomly changing one parameter
    /// </summary>
    private StrategySettings Mutate(StrategySettings settings)
    {
        // Mutate random parameter (10 mutable parameters)
        var paramIndex = _random.Next(10);
        return paramIndex switch
        {
            0 => settings with { AdxPeriod = MutateInt(settings.AdxPeriod, _config.AdxPeriodMin, _config.AdxPeriodMax) },
            1 => settings with { AdxThreshold = MutateDecimal(settings.AdxThreshold, _config.AdxThresholdMin, _config.AdxThresholdMax) },
            2 => settings with { AdxExitThreshold = MutateDecimal(settings.AdxExitThreshold, _config.AdxExitThresholdMin, _config.AdxExitThresholdMax) },
            3 => settings with { FastEmaPeriod = MutateInt(settings.FastEmaPeriod, _config.FastEmaMin, _config.FastEmaMax) },
            4 => settings with { SlowEmaPeriod = MutateInt(settings.SlowEmaPeriod, _config.SlowEmaMin, _config.SlowEmaMax) },
            5 => settings with { AtrStopMultiplier = MutateDecimal(settings.AtrStopMultiplier, _config.AtrMultiplierMin, _config.AtrMultiplierMax) },
            6 => settings with { TakeProfitMultiplier = MutateDecimal(settings.TakeProfitMultiplier, _config.TakeProfitMultiplierMin, _config.TakeProfitMultiplierMax) },
            7 => settings with { VolumeThreshold = MutateDecimal(settings.VolumeThreshold, _config.VolumeThresholdMin, _config.VolumeThresholdMax) },
            8 => settings with { RequireVolumeConfirmation = !settings.RequireVolumeConfirmation },
            _ => settings with { RequireObvConfirmation = !settings.RequireObvConfirmation }
        };
    }

    /// <summary>
    /// Crosses over two parent settings using uniform crossover
    /// </summary>
    private StrategySettings Crossover(StrategySettings parent1, StrategySettings parent2)
    {
        return new StrategySettings
        {
            AdxPeriod = Pick(parent1.AdxPeriod, parent2.AdxPeriod),
            AdxThreshold = Pick(parent1.AdxThreshold, parent2.AdxThreshold),
            AdxExitThreshold = Pick(parent1.AdxExitThreshold, parent2.AdxExitThreshold),
            FastEmaPeriod = Pick(parent1.FastEmaPeriod, parent2.FastEmaPeriod),
            SlowEmaPeriod = Pick(parent1.SlowEmaPeriod, parent2.SlowEmaPeriod),
            AtrPeriod = 14,
            AtrStopMultiplier = Pick(parent1.AtrStopMultiplier, parent2.AtrStopMultiplier),
            TakeProfitMultiplier = Pick(parent1.TakeProfitMultiplier, parent2.TakeProfitMultiplier),
            VolumeThreshold = Pick(parent1.VolumeThreshold, parent2.VolumeThreshold),
            RequireVolumeConfirmation = Pick(parent1.RequireVolumeConfirmation, parent2.RequireVolumeConfirmation),
            RequireObvConfirmation = Pick(parent1.RequireObvConfirmation, parent2.RequireObvConfirmation)
        };
    }

    /// <summary>
    /// Validates that settings are logically consistent
    /// </summary>
    private bool Validate(StrategySettings settings)
    {
        // Fast EMA must be faster than slow EMA
        if (settings.FastEmaPeriod >= settings.SlowEmaPeriod)
            return false;

        // All values must be within reasonable ranges
        if (settings.AdxThreshold < 0 || settings.AdxThreshold > 100)
            return false;

        if (settings.AtrStopMultiplier <= 0 || settings.TakeProfitMultiplier <= 0)
            return false;

        return true;
    }

    /// <summary>
    /// Evaluates fitness of settings by running backtest
    /// </summary>
    private decimal EvaluateFitness(StrategySettings settings, List<Candle> candles, string symbol)
    {
        // Validate first
        if (!Validate(settings))
            return -1000m;

        try
        {
            var strategy = new AdxTrendStrategy(settings);
            var engine = new BacktestEngine(strategy, _riskSettings, _backtestSettings);
            var result = engine.Run(candles, symbol);

            // Require minimum trades
            if (result.Metrics.TotalTrades < _minTrades)
                return -100m;

            // Calculate fitness based on selected function
            return _fitnessFunction switch
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

    // Helper methods
    private int RandomInt(int min, int max) => _random.Next(min, max + 1);

    private decimal RandomDecimal(decimal min, decimal max)
    {
        return min + (decimal)_random.NextDouble() * (max - min);
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

    private T Pick<T>(T a, T b) => _random.NextDouble() > 0.5 ? a : b;
}

/// <summary>
/// Parameter ranges for ADX strategy optimization
/// </summary>
public record AdxOptimizerConfig
{
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

/// <summary>
/// Fitness function types
/// </summary>
public enum FitnessFunction
{
    Sharpe,
    Sortino,
    ProfitFactor,
    Return,
    RiskAdjusted,
    Combined
}
