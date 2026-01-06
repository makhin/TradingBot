using ComplexBot.Models;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Strategies;

namespace ComplexBot.Services.Backtesting;

public class MaStrategyOptimizer : StrategyOptimizerBase<MaStrategySettings, MaOptimizerConfig>
{
    private readonly FitnessFunction _fitnessFunction;

    public MaStrategyOptimizer(
        MaOptimizerConfig? config = null,
        RiskSettings? riskSettings = null,
        BacktestSettings? backtestSettings = null,
        FitnessFunction fitnessFunction = FitnessFunction.RiskAdjusted,
        PerformanceFitnessPolicy? policy = null)
        : base(config ?? new MaOptimizerConfig(), riskSettings, backtestSettings, policy)
    {
        _fitnessFunction = fitnessFunction;
    }

    protected override MaStrategySettings CreateRandom()
    {
        return new MaStrategySettings
        {
            FastMaPeriod = RandomInt(Config.FastMaMin, Config.FastMaMax),
            SlowMaPeriod = RandomInt(Config.SlowMaMin, Config.SlowMaMax),
            AtrPeriod = Config.AtrPeriod,
            AtrStopMultiplier = RandomDecimal(Config.AtrMultiplierMin, Config.AtrMultiplierMax),
            TakeProfitMultiplier = RandomDecimal(Config.TakeProfitMultiplierMin, Config.TakeProfitMultiplierMax),
            VolumePeriod = Config.VolumePeriod,
            VolumeThreshold = RandomDecimal(Config.VolumeThresholdMin, Config.VolumeThresholdMax),
            RequireVolumeConfirmation = Random.NextDouble() > 0.5
        };
    }

    protected override MaStrategySettings Mutate(MaStrategySettings settings)
    {
        var paramIndex = Random.Next(6);
        return paramIndex switch
        {
            0 => settings with { FastMaPeriod = MutateInt(settings.FastMaPeriod, Config.FastMaMin, Config.FastMaMax) },
            1 => settings with { SlowMaPeriod = MutateInt(settings.SlowMaPeriod, Config.SlowMaMin, Config.SlowMaMax) },
            2 => settings with { AtrStopMultiplier = MutateDecimal(settings.AtrStopMultiplier, Config.AtrMultiplierMin, Config.AtrMultiplierMax) },
            3 => settings with { TakeProfitMultiplier = MutateDecimal(settings.TakeProfitMultiplier, Config.TakeProfitMultiplierMin, Config.TakeProfitMultiplierMax) },
            4 => settings with { VolumeThreshold = MutateDecimal(settings.VolumeThreshold, Config.VolumeThresholdMin, Config.VolumeThresholdMax) },
            _ => settings with { RequireVolumeConfirmation = !settings.RequireVolumeConfirmation }
        };
    }

    protected override MaStrategySettings Crossover(MaStrategySettings parent1, MaStrategySettings parent2)
    {
        return new MaStrategySettings
        {
            FastMaPeriod = Pick(parent1.FastMaPeriod, parent2.FastMaPeriod),
            SlowMaPeriod = Pick(parent1.SlowMaPeriod, parent2.SlowMaPeriod),
            AtrPeriod = Config.AtrPeriod,
            AtrStopMultiplier = Pick(parent1.AtrStopMultiplier, parent2.AtrStopMultiplier),
            TakeProfitMultiplier = Pick(parent1.TakeProfitMultiplier, parent2.TakeProfitMultiplier),
            VolumePeriod = Config.VolumePeriod,
            VolumeThreshold = Pick(parent1.VolumeThreshold, parent2.VolumeThreshold),
            RequireVolumeConfirmation = Pick(parent1.RequireVolumeConfirmation, parent2.RequireVolumeConfirmation)
        };
    }

    protected override bool Validate(MaStrategySettings settings)
    {
        if (settings.FastMaPeriod >= settings.SlowMaPeriod)
            return false;
        if (settings.AtrStopMultiplier <= 0 || settings.TakeProfitMultiplier <= 0)
            return false;
        if (settings.VolumeThreshold <= 0)
            return false;

        return true;
    }

    protected override decimal EvaluateFitness(MaStrategySettings settings, List<Candle> candles, string symbol)
    {
        if (!Validate(settings))
            return FitnessCalculator.InvalidSettingsPenalty;

        try
        {
            var strategy = new MaStrategy(settings);
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
