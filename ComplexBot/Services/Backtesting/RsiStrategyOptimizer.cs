using ComplexBot.Models;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Strategies;

namespace ComplexBot.Services.Backtesting;

public class RsiStrategyOptimizer
{
    private readonly RsiOptimizerConfig _config;
    private readonly Random _random;
    private readonly RiskSettings _riskSettings;
    private readonly BacktestSettings _backtestSettings;
    private readonly FitnessFunction _fitnessFunction;
    private readonly int _minTrades;

    public RsiStrategyOptimizer(
        RsiOptimizerConfig? config = null,
        RiskSettings? riskSettings = null,
        BacktestSettings? backtestSettings = null,
        FitnessFunction fitnessFunction = FitnessFunction.RiskAdjusted,
        int minTrades = 30)
    {
        _config = config ?? new RsiOptimizerConfig();
        _random = new Random();
        _riskSettings = riskSettings ?? new RiskSettings();
        _backtestSettings = backtestSettings ?? new BacktestSettings();
        _fitnessFunction = fitnessFunction;
        _minTrades = minTrades;
    }

    public GeneticOptimizer<RsiStrategySettings> CreateOptimizer(GeneticOptimizerSettings? settings = null)
    {
        return new GeneticOptimizer<RsiStrategySettings>(
            createRandom: CreateRandom,
            mutate: Mutate,
            crossover: Crossover,
            validate: Validate,
            settings: settings
        );
    }

    public GeneticOptimizationResult<RsiStrategySettings> Optimize(
        List<Candle> candles,
        string symbol,
        GeneticOptimizerSettings? settings = null,
        IProgress<GeneticProgress<RsiStrategySettings>>? progress = null)
    {
        if (candles == null || candles.Count < 200)
            throw new ArgumentException("Insufficient data for optimization. Required: 200 candles minimum", nameof(candles));

        var optimizer = CreateOptimizer(settings);

        Func<RsiStrategySettings, decimal> evaluateFitness = candidate =>
            EvaluateFitness(candidate, candles, symbol);

        return optimizer.Optimize(evaluateFitness, progress);
    }

    private RsiStrategySettings CreateRandom()
    {
        return new RsiStrategySettings
        {
            RsiPeriod = RandomInt(_config.RsiPeriodMin, _config.RsiPeriodMax),
            OversoldLevel = RandomDecimal(_config.OversoldMin, _config.OversoldMax),
            OverboughtLevel = RandomDecimal(_config.OverboughtMin, _config.OverboughtMax),
            NeutralZoneLow = _config.NeutralZoneLow,
            NeutralZoneHigh = _config.NeutralZoneHigh,
            ExitOnNeutral = _random.NextDouble() > 0.5,
            AtrPeriod = _config.AtrPeriod,
            AtrStopMultiplier = RandomDecimal(_config.AtrMultiplierMin, _config.AtrMultiplierMax),
            TakeProfitMultiplier = RandomDecimal(_config.TakeProfitMultiplierMin, _config.TakeProfitMultiplierMax),
            TrendFilterPeriod = RandomInt(_config.TrendFilterMin, _config.TrendFilterMax),
            UseTrendFilter = _random.NextDouble() > 0.5,
            VolumePeriod = _config.VolumePeriod,
            VolumeThreshold = RandomDecimal(_config.VolumeThresholdMin, _config.VolumeThresholdMax),
            RequireVolumeConfirmation = _random.NextDouble() > 0.5
        };
    }

    private RsiStrategySettings Mutate(RsiStrategySettings settings)
    {
        var paramIndex = _random.Next(10);
        return paramIndex switch
        {
            0 => settings with { RsiPeriod = MutateInt(settings.RsiPeriod, _config.RsiPeriodMin, _config.RsiPeriodMax) },
            1 => settings with { OversoldLevel = MutateDecimal(settings.OversoldLevel, _config.OversoldMin, _config.OversoldMax) },
            2 => settings with { OverboughtLevel = MutateDecimal(settings.OverboughtLevel, _config.OverboughtMin, _config.OverboughtMax) },
            3 => settings with { AtrStopMultiplier = MutateDecimal(settings.AtrStopMultiplier, _config.AtrMultiplierMin, _config.AtrMultiplierMax) },
            4 => settings with { TakeProfitMultiplier = MutateDecimal(settings.TakeProfitMultiplier, _config.TakeProfitMultiplierMin, _config.TakeProfitMultiplierMax) },
            5 => settings with { TrendFilterPeriod = MutateInt(settings.TrendFilterPeriod, _config.TrendFilterMin, _config.TrendFilterMax) },
            6 => settings with { VolumeThreshold = MutateDecimal(settings.VolumeThreshold, _config.VolumeThresholdMin, _config.VolumeThresholdMax) },
            7 => settings with { UseTrendFilter = !settings.UseTrendFilter },
            8 => settings with { ExitOnNeutral = !settings.ExitOnNeutral },
            _ => settings with { RequireVolumeConfirmation = !settings.RequireVolumeConfirmation }
        };
    }

    private RsiStrategySettings Crossover(RsiStrategySettings parent1, RsiStrategySettings parent2)
    {
        return new RsiStrategySettings
        {
            RsiPeriod = Pick(parent1.RsiPeriod, parent2.RsiPeriod),
            OversoldLevel = Pick(parent1.OversoldLevel, parent2.OversoldLevel),
            OverboughtLevel = Pick(parent1.OverboughtLevel, parent2.OverboughtLevel),
            NeutralZoneLow = _config.NeutralZoneLow,
            NeutralZoneHigh = _config.NeutralZoneHigh,
            ExitOnNeutral = Pick(parent1.ExitOnNeutral, parent2.ExitOnNeutral),
            AtrPeriod = _config.AtrPeriod,
            AtrStopMultiplier = Pick(parent1.AtrStopMultiplier, parent2.AtrStopMultiplier),
            TakeProfitMultiplier = Pick(parent1.TakeProfitMultiplier, parent2.TakeProfitMultiplier),
            TrendFilterPeriod = Pick(parent1.TrendFilterPeriod, parent2.TrendFilterPeriod),
            UseTrendFilter = Pick(parent1.UseTrendFilter, parent2.UseTrendFilter),
            VolumePeriod = _config.VolumePeriod,
            VolumeThreshold = Pick(parent1.VolumeThreshold, parent2.VolumeThreshold),
            RequireVolumeConfirmation = Pick(parent1.RequireVolumeConfirmation, parent2.RequireVolumeConfirmation)
        };
    }

    private bool Validate(RsiStrategySettings settings)
    {
        if (settings.OversoldLevel >= settings.OverboughtLevel)
            return false;
        if (settings.OversoldLevel >= 50 || settings.OverboughtLevel <= 50)
            return false;
        if (settings.NeutralZoneLow >= settings.NeutralZoneHigh)
            return false;
        if (settings.AtrStopMultiplier <= 0 || settings.TakeProfitMultiplier <= 0)
            return false;
        if (settings.VolumeThreshold <= 0)
            return false;

        return true;
    }

    private decimal EvaluateFitness(RsiStrategySettings settings, List<Candle> candles, string symbol)
    {
        if (!Validate(settings))
            return -1000m;

        try
        {
            var strategy = new RsiStrategy(settings);
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

public record RsiOptimizerConfig
{
    public int RsiPeriodMin { get; init; } = 10;
    public int RsiPeriodMax { get; init; } = 20;
    public decimal OversoldMin { get; init; } = 20m;
    public decimal OversoldMax { get; init; } = 35m;
    public decimal OverboughtMin { get; init; } = 65m;
    public decimal OverboughtMax { get; init; } = 80m;
    public decimal NeutralZoneLow { get; init; } = 45m;
    public decimal NeutralZoneHigh { get; init; } = 55m;
    public int AtrPeriod { get; init; } = 14;
    public decimal AtrMultiplierMin { get; init; } = 1.0m;
    public decimal AtrMultiplierMax { get; init; } = 3.5m;
    public decimal TakeProfitMultiplierMin { get; init; } = 1.5m;
    public decimal TakeProfitMultiplierMax { get; init; } = 3.0m;
    public int TrendFilterMin { get; init; } = 20;
    public int TrendFilterMax { get; init; } = 100;
    public int VolumePeriod { get; init; } = 20;
    public decimal VolumeThresholdMin { get; init; } = 1.0m;
    public decimal VolumeThresholdMax { get; init; } = 2.5m;
}
