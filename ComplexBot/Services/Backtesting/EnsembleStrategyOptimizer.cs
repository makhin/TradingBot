using ComplexBot.Models;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Strategies;

namespace ComplexBot.Services.Backtesting;

public class EnsembleStrategyOptimizer
{
    private readonly EnsembleOptimizerConfig _config;
    private readonly Random _random;
    private readonly RiskSettings _riskSettings;
    private readonly BacktestSettings _backtestSettings;
    private readonly OptimizationTarget _optimizeFor;
    private readonly int _minTrades;

    public EnsembleStrategyOptimizer(
        EnsembleOptimizerConfig? config = null,
        RiskSettings? riskSettings = null,
        BacktestSettings? backtestSettings = null,
        OptimizationTarget optimizeFor = OptimizationTarget.RiskAdjusted,
        int? minTrades = null)
    {
        _config = config ?? new EnsembleOptimizerConfig();
        _random = new Random();
        _riskSettings = riskSettings ?? new RiskSettings();
        _backtestSettings = backtestSettings ?? new BacktestSettings();
        _optimizeFor = optimizeFor;
        _minTrades = minTrades ?? _config.MinTrades;
    }

    public GeneticOptimizer<EnsembleOptimizationSettings> CreateOptimizer(
        GeneticOptimizerSettings? settings = null)
    {
        return new GeneticOptimizer<EnsembleOptimizationSettings>(
            createRandom: CreateRandom,
            mutate: Mutate,
            crossover: Crossover,
            validate: Validate,
            settings: settings
        );
    }

    public GeneticOptimizationResult<EnsembleOptimizationSettings> Optimize(
        List<Candle> candles,
        string symbol,
        GeneticOptimizerSettings? settings = null,
        IProgress<GeneticProgress<EnsembleOptimizationSettings>>? progress = null)
    {
        if (candles == null || candles.Count < 200)
            throw new ArgumentException("Insufficient data for optimization. Required: 200 candles minimum", nameof(candles));

        var optimizer = CreateOptimizer(settings);

        Func<EnsembleOptimizationSettings, decimal> evaluateFitness = candidate =>
            EvaluateFitness(candidate, candles, symbol);

        return optimizer.Optimize(evaluateFitness, progress);
    }

    private EnsembleOptimizationSettings CreateRandom()
    {
        return new EnsembleOptimizationSettings
        {
            AdxWeight = RandomDecimal(_config.WeightMin, _config.WeightMax),
            MaWeight = RandomDecimal(_config.WeightMin, _config.WeightMax),
            RsiWeight = RandomDecimal(_config.WeightMin, _config.WeightMax),
            MinimumAgreement = RandomDecimal(_config.MinimumAgreementMin, _config.MinimumAgreementMax),
            UseConfidenceWeighting = _config.AllowConfidenceWeightingToggle
                ? _random.NextDouble() > 0.5
                : _config.DefaultUseConfidenceWeighting
        };
    }

    private EnsembleOptimizationSettings Mutate(EnsembleOptimizationSettings settings)
    {
        var paramCount = _config.AllowConfidenceWeightingToggle ? 5 : 4;
        var paramIndex = _random.Next(paramCount);

        return paramIndex switch
        {
            0 => settings with { AdxWeight = MutateDecimal(settings.AdxWeight, _config.WeightMin, _config.WeightMax) },
            1 => settings with { MaWeight = MutateDecimal(settings.MaWeight, _config.WeightMin, _config.WeightMax) },
            2 => settings with { RsiWeight = MutateDecimal(settings.RsiWeight, _config.WeightMin, _config.WeightMax) },
            3 => settings with { MinimumAgreement = MutateDecimal(settings.MinimumAgreement, _config.MinimumAgreementMin, _config.MinimumAgreementMax) },
            _ => settings with { UseConfidenceWeighting = !settings.UseConfidenceWeighting }
        };
    }

    private EnsembleOptimizationSettings Crossover(
        EnsembleOptimizationSettings parent1,
        EnsembleOptimizationSettings parent2)
    {
        return new EnsembleOptimizationSettings
        {
            AdxWeight = Pick(parent1.AdxWeight, parent2.AdxWeight),
            MaWeight = Pick(parent1.MaWeight, parent2.MaWeight),
            RsiWeight = Pick(parent1.RsiWeight, parent2.RsiWeight),
            MinimumAgreement = Pick(parent1.MinimumAgreement, parent2.MinimumAgreement),
            UseConfidenceWeighting = Pick(parent1.UseConfidenceWeighting, parent2.UseConfidenceWeighting)
        };
    }

    private bool Validate(EnsembleOptimizationSettings settings)
    {
        if (!IsInRange(settings.AdxWeight, _config.WeightMin, _config.WeightMax))
            return false;
        if (!IsInRange(settings.MaWeight, _config.WeightMin, _config.WeightMax))
            return false;
        if (!IsInRange(settings.RsiWeight, _config.WeightMin, _config.WeightMax))
            return false;
        if (!IsInRange(settings.MinimumAgreement, _config.MinimumAgreementMin, _config.MinimumAgreementMax))
            return false;

        return true;
    }

    private decimal EvaluateFitness(
        EnsembleOptimizationSettings settings,
        List<Candle> candles,
        string symbol)
    {
        if (!Validate(settings))
            return -1000m;

        try
        {
            var strategy = StrategyEnsemble.CreateDefault(settings.ToEnsembleSettings());
            var engine = new BacktestEngine(strategy, _riskSettings, _backtestSettings);
            var result = engine.Run(candles, symbol);

            if (result.Metrics.TotalTrades < _minTrades)
                return -100m;

            return CalculateScore(result.Metrics);
        }
        catch
        {
            return -1000m;
        }
    }

    private decimal CalculateScore(PerformanceMetrics metrics)
    {
        if (metrics.TotalTrades < _minTrades)
            return -100m;

        return _optimizeFor switch
        {
            OptimizationTarget.SharpeRatio => metrics.SharpeRatio,
            OptimizationTarget.SortinoRatio => metrics.SortinoRatio,
            OptimizationTarget.ProfitFactor => metrics.ProfitFactor,
            OptimizationTarget.TotalReturn => metrics.TotalReturn,
            OptimizationTarget.RiskAdjusted =>
                metrics.AnnualizedReturn / (metrics.MaxDrawdownPercent + 1) * (metrics.SharpeRatio + 1),
            _ => metrics.SharpeRatio
        };
    }

    private decimal RandomDecimal(decimal min, decimal max)
    {
        return min + (decimal)_random.NextDouble() * (max - min);
    }

    private decimal MutateDecimal(decimal value, decimal min, decimal max)
    {
        var delta = (max - min) / 4;
        var newValue = value + (decimal)(_random.NextDouble() * 2 - 1) * delta;
        return Math.Clamp(newValue, min, max);
    }

    private static bool IsInRange(decimal value, decimal min, decimal max) =>
        value >= min && value <= max;

    private T Pick<T>(T a, T b) => _random.NextDouble() > 0.5 ? a : b;
}

public record EnsembleOptimizationSettings
{
    public decimal AdxWeight { get; init; }
    public decimal MaWeight { get; init; }
    public decimal RsiWeight { get; init; }
    public decimal MinimumAgreement { get; init; }
    public bool UseConfidenceWeighting { get; init; }

    public EnsembleSettings ToEnsembleSettings() => new()
    {
        MinimumAgreement = MinimumAgreement,
        UseConfidenceWeighting = UseConfidenceWeighting,
        StrategyWeights = new Dictionary<string, decimal>
        {
            ["ADX Trend Following + Volume"] = AdxWeight,
            ["MA Crossover"] = MaWeight,
            ["RSI Mean Reversion"] = RsiWeight
        }
    };
}

public record EnsembleOptimizerConfig
{
    public decimal WeightMin { get; init; } = 0.05m;
    public decimal WeightMax { get; init; } = 1.0m;
    public decimal MinimumAgreementMin { get; init; } = 0.4m;
    public decimal MinimumAgreementMax { get; init; } = 0.8m;
    public bool AllowConfidenceWeightingToggle { get; init; } = true;
    public bool DefaultUseConfidenceWeighting { get; init; } = true;
    public int MinTrades { get; init; } = 20;
}
