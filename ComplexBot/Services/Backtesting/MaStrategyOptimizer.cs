using ComplexBot.Models;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Strategies;

namespace ComplexBot.Services.Backtesting;

public class MaStrategyOptimizer
{
    private readonly MaOptimizerConfig _config;
    private readonly Random _random;
    private readonly RiskSettings _riskSettings;
    private readonly BacktestSettings _backtestSettings;
    private readonly FitnessFunction _fitnessFunction;
    private readonly int _minTrades;

    public MaStrategyOptimizer(
        MaOptimizerConfig? config = null,
        RiskSettings? riskSettings = null,
        BacktestSettings? backtestSettings = null,
        FitnessFunction fitnessFunction = FitnessFunction.RiskAdjusted,
        int minTrades = 30)
    {
        _config = config ?? new MaOptimizerConfig();
        _random = new Random();
        _riskSettings = riskSettings ?? new RiskSettings();
        _backtestSettings = backtestSettings ?? new BacktestSettings();
        _fitnessFunction = fitnessFunction;
        _minTrades = minTrades;
    }

    public GeneticOptimizer<MaStrategySettings> CreateOptimizer(GeneticOptimizerSettings? settings = null)
    {
        return new GeneticOptimizer<MaStrategySettings>(
            createRandom: CreateRandom,
            mutate: Mutate,
            crossover: Crossover,
            validate: Validate,
            settings: settings
        );
    }

    public GeneticOptimizationResult<MaStrategySettings> Optimize(
        List<Candle> candles,
        string symbol,
        GeneticOptimizerSettings? settings = null,
        IProgress<GeneticProgress<MaStrategySettings>>? progress = null)
    {
        if (candles == null || candles.Count < 200)
            throw new ArgumentException("Insufficient data for optimization. Required: 200 candles minimum", nameof(candles));

        var optimizer = CreateOptimizer(settings);

        Func<MaStrategySettings, decimal> evaluateFitness = candidate =>
            EvaluateFitness(candidate, candles, symbol);

        return optimizer.Optimize(evaluateFitness, progress);
    }

    private MaStrategySettings CreateRandom()
    {
        return new MaStrategySettings
        {
            FastMaPeriod = RandomInt(_config.FastMaMin, _config.FastMaMax),
            SlowMaPeriod = RandomInt(_config.SlowMaMin, _config.SlowMaMax),
            AtrPeriod = _config.AtrPeriod,
            AtrStopMultiplier = RandomDecimal(_config.AtrMultiplierMin, _config.AtrMultiplierMax),
            TakeProfitMultiplier = RandomDecimal(_config.TakeProfitMultiplierMin, _config.TakeProfitMultiplierMax),
            VolumePeriod = _config.VolumePeriod,
            VolumeThreshold = RandomDecimal(_config.VolumeThresholdMin, _config.VolumeThresholdMax),
            RequireVolumeConfirmation = _random.NextDouble() > 0.5
        };
    }

    private MaStrategySettings Mutate(MaStrategySettings settings)
    {
        var paramIndex = _random.Next(6);
        return paramIndex switch
        {
            0 => settings with { FastMaPeriod = MutateInt(settings.FastMaPeriod, _config.FastMaMin, _config.FastMaMax) },
            1 => settings with { SlowMaPeriod = MutateInt(settings.SlowMaPeriod, _config.SlowMaMin, _config.SlowMaMax) },
            2 => settings with { AtrStopMultiplier = MutateDecimal(settings.AtrStopMultiplier, _config.AtrMultiplierMin, _config.AtrMultiplierMax) },
            3 => settings with { TakeProfitMultiplier = MutateDecimal(settings.TakeProfitMultiplier, _config.TakeProfitMultiplierMin, _config.TakeProfitMultiplierMax) },
            4 => settings with { VolumeThreshold = MutateDecimal(settings.VolumeThreshold, _config.VolumeThresholdMin, _config.VolumeThresholdMax) },
            _ => settings with { RequireVolumeConfirmation = !settings.RequireVolumeConfirmation }
        };
    }

    private MaStrategySettings Crossover(MaStrategySettings parent1, MaStrategySettings parent2)
    {
        return new MaStrategySettings
        {
            FastMaPeriod = Pick(parent1.FastMaPeriod, parent2.FastMaPeriod),
            SlowMaPeriod = Pick(parent1.SlowMaPeriod, parent2.SlowMaPeriod),
            AtrPeriod = _config.AtrPeriod,
            AtrStopMultiplier = Pick(parent1.AtrStopMultiplier, parent2.AtrStopMultiplier),
            TakeProfitMultiplier = Pick(parent1.TakeProfitMultiplier, parent2.TakeProfitMultiplier),
            VolumePeriod = _config.VolumePeriod,
            VolumeThreshold = Pick(parent1.VolumeThreshold, parent2.VolumeThreshold),
            RequireVolumeConfirmation = Pick(parent1.RequireVolumeConfirmation, parent2.RequireVolumeConfirmation)
        };
    }

    private bool Validate(MaStrategySettings settings)
    {
        if (settings.FastMaPeriod >= settings.SlowMaPeriod)
            return false;
        if (settings.AtrStopMultiplier <= 0 || settings.TakeProfitMultiplier <= 0)
            return false;
        if (settings.VolumeThreshold <= 0)
            return false;

        return true;
    }

    private decimal EvaluateFitness(MaStrategySettings settings, List<Candle> candles, string symbol)
    {
        if (!Validate(settings))
            return -1000m;

        try
        {
            var strategy = new MaStrategy(settings);
            var engine = new BacktestEngine(strategy, _riskSettings, _backtestSettings);
            var result = engine.Run(candles, symbol);

            if (result.Metrics.TotalTrades < _minTrades)
                return -100m;

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

    private static decimal CalculateRiskAdjustedFitness(PerformanceMetrics metrics)
    {
        var drawdownPenalty = Math.Max(0, metrics.MaxDrawdownPercent - 20) * 0.1m;
        return metrics.SharpeRatio - drawdownPenalty;
    }

    private static decimal CalculateCombinedFitness(PerformanceMetrics metrics)
    {
        var sharpeComponent = Math.Max(0, metrics.SharpeRatio);
        var pfComponent = 1 + Math.Min(3, metrics.ProfitFactor) / 10;
        var ddComponent = 1 - Math.Min(1, metrics.MaxDrawdownPercent / 100);

        return sharpeComponent * pfComponent * ddComponent;
    }

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
