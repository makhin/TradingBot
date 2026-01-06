using ComplexBot.Models;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.RiskManagement;

namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Optimizer configuration specifically for ADX Trend Following strategy
/// Provides parameter ranges and genetic operators for StrategySettings
/// </summary>
public class AdxStrategyOptimizer : StrategyOptimizerBase<StrategySettings, AdxOptimizerConfig>
{
    private readonly FitnessFunction _fitnessFunction;

    public AdxStrategyOptimizer(
        AdxOptimizerConfig? config = null,
        RiskSettings? riskSettings = null,
        BacktestSettings? backtestSettings = null,
        FitnessFunction fitnessFunction = FitnessFunction.RiskAdjusted,
        PerformanceFitnessPolicy? policy = null)
        : base(config ?? new AdxOptimizerConfig(), riskSettings, backtestSettings, policy)
    {
        _fitnessFunction = fitnessFunction;
    }

    /// <summary>
    /// Creates random settings within configured ranges
    /// </summary>
    protected override StrategySettings CreateRandom()
    {
        return new StrategySettings
        {
            AdxPeriod = RandomInt(Config.AdxPeriodMin, Config.AdxPeriodMax),
            AdxThreshold = RandomDecimal(Config.AdxThresholdMin, Config.AdxThresholdMax),
            AdxExitThreshold = RandomDecimal(Config.AdxExitThresholdMin, Config.AdxExitThresholdMax),
            FastEmaPeriod = RandomInt(Config.FastEmaMin, Config.FastEmaMax),
            SlowEmaPeriod = RandomInt(Config.SlowEmaMin, Config.SlowEmaMax),
            AtrPeriod = 14,
            AtrStopMultiplier = RandomDecimal(Config.AtrMultiplierMin, Config.AtrMultiplierMax),
            TakeProfitMultiplier = RandomDecimal(Config.TakeProfitMultiplierMin, Config.TakeProfitMultiplierMax),
            VolumeThreshold = RandomDecimal(Config.VolumeThresholdMin, Config.VolumeThresholdMax),
            RequireVolumeConfirmation = Random.NextDouble() > 0.5,
            RequireObvConfirmation = Random.NextDouble() > 0.5
        };
    }

    /// <summary>
    /// Mutates settings by randomly changing one parameter
    /// </summary>
    protected override StrategySettings Mutate(StrategySettings settings)
    {
        // Mutate random parameter (10 mutable parameters)
        var paramIndex = Random.Next(10);
        return paramIndex switch
        {
            0 => settings with { AdxPeriod = MutateInt(settings.AdxPeriod, Config.AdxPeriodMin, Config.AdxPeriodMax) },
            1 => settings with { AdxThreshold = MutateDecimal(settings.AdxThreshold, Config.AdxThresholdMin, Config.AdxThresholdMax) },
            2 => settings with { AdxExitThreshold = MutateDecimal(settings.AdxExitThreshold, Config.AdxExitThresholdMin, Config.AdxExitThresholdMax) },
            3 => settings with { FastEmaPeriod = MutateInt(settings.FastEmaPeriod, Config.FastEmaMin, Config.FastEmaMax) },
            4 => settings with { SlowEmaPeriod = MutateInt(settings.SlowEmaPeriod, Config.SlowEmaMin, Config.SlowEmaMax) },
            5 => settings with { AtrStopMultiplier = MutateDecimal(settings.AtrStopMultiplier, Config.AtrMultiplierMin, Config.AtrMultiplierMax) },
            6 => settings with { TakeProfitMultiplier = MutateDecimal(settings.TakeProfitMultiplier, Config.TakeProfitMultiplierMin, Config.TakeProfitMultiplierMax) },
            7 => settings with { VolumeThreshold = MutateDecimal(settings.VolumeThreshold, Config.VolumeThresholdMin, Config.VolumeThresholdMax) },
            8 => settings with { RequireVolumeConfirmation = !settings.RequireVolumeConfirmation },
            _ => settings with { RequireObvConfirmation = !settings.RequireObvConfirmation }
        };
    }

    /// <summary>
    /// Crosses over two parent settings using uniform crossover
    /// </summary>
    protected override StrategySettings Crossover(StrategySettings parent1, StrategySettings parent2)
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
    protected override bool Validate(StrategySettings settings)
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
    protected override decimal EvaluateFitness(StrategySettings settings, List<Candle> candles, string symbol)
    {
        // Validate first
        if (!Validate(settings))
            return FitnessCalculator.InvalidSettingsPenalty;

        try
        {
            var strategy = new AdxTrendStrategy(settings);
            var engine = new BacktestEngine(strategy, RiskSettings, BacktestSettings);
            var result = engine.Run(candles, symbol);

            return FitnessCalculator.CalculateFitness(_fitnessFunction, result.Metrics);
        }
        catch
        {
            return FitnessCalculator.InvalidSettingsPenalty;
        }
    }
}
