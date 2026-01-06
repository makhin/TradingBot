using ComplexBot.Models;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Strategies;

namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Full Ensemble Optimizer - optimizes both strategy weights AND individual strategy parameters.
/// This creates a much larger search space (~25 parameters) but can find better combinations.
/// </summary>
public class FullEnsembleOptimizer
{
    private readonly FullEnsembleOptimizerConfig _config;
    private readonly Random _random;
    private readonly RiskSettings _riskSettings;
    private readonly BacktestSettings _backtestSettings;
    private readonly OptimizationTarget _optimizeFor;
    private readonly PerformanceFitnessCalculator _fitnessCalculator;

    public FullEnsembleOptimizer(
        FullEnsembleOptimizerConfig? config = null,
        RiskSettings? riskSettings = null,
        BacktestSettings? backtestSettings = null,
        OptimizationTarget optimizeFor = OptimizationTarget.RiskAdjusted,
        PerformanceFitnessPolicy? policy = null)
    {
        _config = config ?? new FullEnsembleOptimizerConfig();
        _random = new Random();
        _riskSettings = riskSettings ?? new RiskSettings();
        _backtestSettings = backtestSettings ?? new BacktestSettings();
        _optimizeFor = optimizeFor;
        var resolvedPolicy = policy ?? new PerformanceFitnessPolicy { MinTrades = 20 };
        _fitnessCalculator = new PerformanceFitnessCalculator(resolvedPolicy);
    }

    public GeneticOptimizer<FullEnsembleSettings> CreateOptimizer(
        GeneticOptimizerSettings? settings = null)
    {
        return new GeneticOptimizer<FullEnsembleSettings>(
            createRandom: CreateRandom,
            mutate: Mutate,
            crossover: Crossover,
            validate: Validate,
            settings: settings
        );
    }

    public GeneticOptimizationResult<FullEnsembleSettings> Optimize(
        List<Candle> candles,
        string symbol,
        GeneticOptimizerSettings? settings = null,
        IProgress<GeneticProgress<FullEnsembleSettings>>? progress = null)
    {
        if (candles == null || candles.Count < 200)
            throw new ArgumentException("Insufficient data. Required: 200 candles minimum", nameof(candles));

        var optimizer = CreateOptimizer(settings);

        Func<FullEnsembleSettings, decimal> evaluateFitness = candidate =>
            EvaluateFitness(candidate, candles, symbol);

        return optimizer.Optimize(evaluateFitness, progress);
    }

    private FullEnsembleSettings CreateRandom()
    {
        return new FullEnsembleSettings
        {
            // Ensemble weights
            AdxWeight = RandomDecimal(_config.WeightMin, _config.WeightMax),
            MaWeight = RandomDecimal(_config.WeightMin, _config.WeightMax),
            RsiWeight = RandomDecimal(_config.WeightMin, _config.WeightMax),
            MinimumAgreement = RandomDecimal(0.4m, 0.8m),
            UseConfidenceWeighting = _random.NextDouble() > 0.5,

            // ADX Strategy parameters
            AdxPeriod = _random.Next(10, 21),
            AdxThreshold = RandomDecimal(20m, 35m),
            AdxExitThreshold = RandomDecimal(15m, 25m),
            AdxFastEmaPeriod = _random.Next(10, 30),
            AdxSlowEmaPeriod = _random.Next(40, 80),
            AdxAtrStopMultiplier = RandomDecimal(1.5m, 4.0m),
            AdxVolumeThreshold = RandomDecimal(1.0m, 2.5m),

            // MA Strategy parameters
            MaFastPeriod = _random.Next(5, 20),
            MaSlowPeriod = _random.Next(25, 60),
            MaAtrStopMultiplier = RandomDecimal(1.5m, 3.5m),
            MaTakeProfitMultiplier = RandomDecimal(1.5m, 3.0m),
            MaVolumeThreshold = RandomDecimal(1.0m, 2.0m),

            // RSI Strategy parameters
            RsiPeriod = _random.Next(10, 21),
            RsiOversoldLevel = RandomDecimal(20m, 35m),
            RsiOverboughtLevel = RandomDecimal(65m, 80m),
            RsiAtrStopMultiplier = RandomDecimal(1.0m, 3.0m),
            RsiTakeProfitMultiplier = RandomDecimal(1.5m, 3.0m),
            RsiUseTrendFilter = _random.NextDouble() > 0.5
        };
    }

    private FullEnsembleSettings Mutate(FullEnsembleSettings settings)
    {
        // 25 parameters to mutate
        var paramIndex = _random.Next(25);

        return paramIndex switch
        {
            // Ensemble weights
            0 => settings with { AdxWeight = MutateDecimal(settings.AdxWeight, _config.WeightMin, _config.WeightMax) },
            1 => settings with { MaWeight = MutateDecimal(settings.MaWeight, _config.WeightMin, _config.WeightMax) },
            2 => settings with { RsiWeight = MutateDecimal(settings.RsiWeight, _config.WeightMin, _config.WeightMax) },
            3 => settings with { MinimumAgreement = MutateDecimal(settings.MinimumAgreement, 0.4m, 0.8m) },
            4 => settings with { UseConfidenceWeighting = !settings.UseConfidenceWeighting },

            // ADX parameters
            5 => settings with { AdxPeriod = MutateInt(settings.AdxPeriod, 10, 20) },
            6 => settings with { AdxThreshold = MutateDecimal(settings.AdxThreshold, 20m, 35m) },
            7 => settings with { AdxExitThreshold = MutateDecimal(settings.AdxExitThreshold, 15m, 25m) },
            8 => settings with { AdxFastEmaPeriod = MutateInt(settings.AdxFastEmaPeriod, 10, 30) },
            9 => settings with { AdxSlowEmaPeriod = MutateInt(settings.AdxSlowEmaPeriod, 40, 80) },
            10 => settings with { AdxAtrStopMultiplier = MutateDecimal(settings.AdxAtrStopMultiplier, 1.5m, 4.0m) },
            11 => settings with { AdxVolumeThreshold = MutateDecimal(settings.AdxVolumeThreshold, 1.0m, 2.5m) },

            // MA parameters
            12 => settings with { MaFastPeriod = MutateInt(settings.MaFastPeriod, 5, 20) },
            13 => settings with { MaSlowPeriod = MutateInt(settings.MaSlowPeriod, 25, 60) },
            14 => settings with { MaAtrStopMultiplier = MutateDecimal(settings.MaAtrStopMultiplier, 1.5m, 3.5m) },
            15 => settings with { MaTakeProfitMultiplier = MutateDecimal(settings.MaTakeProfitMultiplier, 1.5m, 3.0m) },
            16 => settings with { MaVolumeThreshold = MutateDecimal(settings.MaVolumeThreshold, 1.0m, 2.0m) },

            // RSI parameters
            17 => settings with { RsiPeriod = MutateInt(settings.RsiPeriod, 10, 20) },
            18 => settings with { RsiOversoldLevel = MutateDecimal(settings.RsiOversoldLevel, 20m, 35m) },
            19 => settings with { RsiOverboughtLevel = MutateDecimal(settings.RsiOverboughtLevel, 65m, 80m) },
            20 => settings with { RsiAtrStopMultiplier = MutateDecimal(settings.RsiAtrStopMultiplier, 1.0m, 3.0m) },
            21 => settings with { RsiTakeProfitMultiplier = MutateDecimal(settings.RsiTakeProfitMultiplier, 1.5m, 3.0m) },
            22 => settings with { RsiUseTrendFilter = !settings.RsiUseTrendFilter },

            // Additional mutations for diversity
            23 => settings with
            {
                AdxThreshold = MutateDecimal(settings.AdxThreshold, 20m, 35m),
                AdxExitThreshold = Math.Min(settings.AdxThreshold - 5, MutateDecimal(settings.AdxExitThreshold, 15m, 25m))
            },
            _ => settings with
            {
                MaFastPeriod = MutateInt(settings.MaFastPeriod, 5, 20),
                MaSlowPeriod = Math.Max(settings.MaFastPeriod + 10, MutateInt(settings.MaSlowPeriod, 25, 60))
            }
        };
    }

    private FullEnsembleSettings Crossover(FullEnsembleSettings p1, FullEnsembleSettings p2)
    {
        return new FullEnsembleSettings
        {
            // Ensemble weights
            AdxWeight = Pick(p1.AdxWeight, p2.AdxWeight),
            MaWeight = Pick(p1.MaWeight, p2.MaWeight),
            RsiWeight = Pick(p1.RsiWeight, p2.RsiWeight),
            MinimumAgreement = Pick(p1.MinimumAgreement, p2.MinimumAgreement),
            UseConfidenceWeighting = Pick(p1.UseConfidenceWeighting, p2.UseConfidenceWeighting),

            // ADX parameters - tend to keep together for consistency
            AdxPeriod = Pick(p1.AdxPeriod, p2.AdxPeriod),
            AdxThreshold = Pick(p1.AdxThreshold, p2.AdxThreshold),
            AdxExitThreshold = Pick(p1.AdxExitThreshold, p2.AdxExitThreshold),
            AdxFastEmaPeriod = Pick(p1.AdxFastEmaPeriod, p2.AdxFastEmaPeriod),
            AdxSlowEmaPeriod = Pick(p1.AdxSlowEmaPeriod, p2.AdxSlowEmaPeriod),
            AdxAtrStopMultiplier = Pick(p1.AdxAtrStopMultiplier, p2.AdxAtrStopMultiplier),
            AdxVolumeThreshold = Pick(p1.AdxVolumeThreshold, p2.AdxVolumeThreshold),

            // MA parameters
            MaFastPeriod = Pick(p1.MaFastPeriod, p2.MaFastPeriod),
            MaSlowPeriod = Pick(p1.MaSlowPeriod, p2.MaSlowPeriod),
            MaAtrStopMultiplier = Pick(p1.MaAtrStopMultiplier, p2.MaAtrStopMultiplier),
            MaTakeProfitMultiplier = Pick(p1.MaTakeProfitMultiplier, p2.MaTakeProfitMultiplier),
            MaVolumeThreshold = Pick(p1.MaVolumeThreshold, p2.MaVolumeThreshold),

            // RSI parameters
            RsiPeriod = Pick(p1.RsiPeriod, p2.RsiPeriod),
            RsiOversoldLevel = Pick(p1.RsiOversoldLevel, p2.RsiOversoldLevel),
            RsiOverboughtLevel = Pick(p1.RsiOverboughtLevel, p2.RsiOverboughtLevel),
            RsiAtrStopMultiplier = Pick(p1.RsiAtrStopMultiplier, p2.RsiAtrStopMultiplier),
            RsiTakeProfitMultiplier = Pick(p1.RsiTakeProfitMultiplier, p2.RsiTakeProfitMultiplier),
            RsiUseTrendFilter = Pick(p1.RsiUseTrendFilter, p2.RsiUseTrendFilter)
        };
    }

    private bool Validate(FullEnsembleSettings s)
    {
        // Basic weight validation
        if (s.AdxWeight < _config.WeightMin || s.AdxWeight > _config.WeightMax) return false;
        if (s.MaWeight < _config.WeightMin || s.MaWeight > _config.WeightMax) return false;
        if (s.RsiWeight < _config.WeightMin || s.RsiWeight > _config.WeightMax) return false;

        // ADX validation
        if (s.AdxExitThreshold >= s.AdxThreshold) return false;
        if (s.AdxFastEmaPeriod >= s.AdxSlowEmaPeriod) return false;

        // MA validation
        if (s.MaFastPeriod >= s.MaSlowPeriod) return false;

        // RSI validation
        if (s.RsiOversoldLevel >= 50) return false;
        if (s.RsiOverboughtLevel <= 50) return false;
        if (s.RsiOversoldLevel >= s.RsiOverboughtLevel) return false;

        return true;
    }

    private decimal EvaluateFitness(FullEnsembleSettings settings, List<Candle> candles, string symbol)
    {
        if (!Validate(settings))
            return _fitnessCalculator.InvalidSettingsPenalty;

        try
        {
            var ensemble = CreateEnsembleFromSettings(settings);
            var engine = new BacktestEngine(ensemble, _riskSettings, _backtestSettings);
            var result = engine.Run(candles, symbol);

            return CalculateScore(result.Metrics);
        }
        catch
        {
            return _fitnessCalculator.InvalidSettingsPenalty;
        }
    }

    /// <summary>
    /// Creates a StrategyEnsemble with custom settings for each strategy
    /// </summary>
    public static StrategyEnsemble CreateEnsembleFromSettings(FullEnsembleSettings s)
    {
        var ensembleSettings = new EnsembleSettings
        {
            MinimumAgreement = s.MinimumAgreement,
            UseConfidenceWeighting = s.UseConfidenceWeighting,
            StrategyWeights = new Dictionary<string, decimal>
            {
                ["ADX Trend Following + Volume"] = s.AdxWeight,
                ["MA Crossover"] = s.MaWeight,
                ["RSI Mean Reversion"] = s.RsiWeight
            }
        };

        var ensemble = new StrategyEnsemble(ensembleSettings);

        // ADX Strategy with custom settings
        var adxSettings = new StrategySettings
        {
            AdxPeriod = s.AdxPeriod,
            AdxThreshold = s.AdxThreshold,
            AdxExitThreshold = s.AdxExitThreshold,
            FastEmaPeriod = s.AdxFastEmaPeriod,
            SlowEmaPeriod = s.AdxSlowEmaPeriod,
            AtrStopMultiplier = s.AdxAtrStopMultiplier,
            VolumeThreshold = s.AdxVolumeThreshold,
            RequireVolumeConfirmation = s.AdxVolumeThreshold > 1.0m
        };
        ensemble.AddStrategy(new AdxTrendStrategy(adxSettings), s.AdxWeight);

        // MA Strategy with custom settings
        var maSettings = new MaStrategySettings
        {
            FastMaPeriod = s.MaFastPeriod,
            SlowMaPeriod = s.MaSlowPeriod,
            AtrStopMultiplier = s.MaAtrStopMultiplier,
            TakeProfitMultiplier = s.MaTakeProfitMultiplier,
            VolumeThreshold = s.MaVolumeThreshold,
            RequireVolumeConfirmation = s.MaVolumeThreshold > 1.0m
        };
        ensemble.AddStrategy(new MaStrategy(maSettings), s.MaWeight);

        // RSI Strategy with custom settings
        var rsiSettings = new RsiStrategySettings
        {
            RsiPeriod = s.RsiPeriod,
            OversoldLevel = s.RsiOversoldLevel,
            OverboughtLevel = s.RsiOverboughtLevel,
            AtrStopMultiplier = s.RsiAtrStopMultiplier,
            TakeProfitMultiplier = s.RsiTakeProfitMultiplier,
            UseTrendFilter = s.RsiUseTrendFilter
        };
        ensemble.AddStrategy(new RsiStrategy(rsiSettings), s.RsiWeight);

        return ensemble;
    }

    private decimal CalculateScore(PerformanceMetrics metrics)
    {
        return _fitnessCalculator.CalculateScore(_optimizeFor, metrics);
    }

    private decimal RandomDecimal(decimal min, decimal max) =>
        min + (decimal)_random.NextDouble() * (max - min);

    private decimal MutateDecimal(decimal value, decimal min, decimal max)
    {
        var delta = (max - min) / 4;
        var newValue = value + (decimal)(_random.NextDouble() * 2 - 1) * delta;
        return Math.Clamp(newValue, min, max);
    }

    private int MutateInt(int value, int min, int max)
    {
        var delta = Math.Max(1, (max - min) / 4);
        var newValue = value + _random.Next(-delta, delta + 1);
        return Math.Clamp(newValue, min, max);
    }

    private T Pick<T>(T a, T b) => _random.NextDouble() > 0.5 ? a : b;
}
