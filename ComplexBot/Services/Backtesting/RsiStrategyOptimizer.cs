using ComplexBot.Models;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Strategies;

namespace ComplexBot.Services.Backtesting;

public class RsiStrategyOptimizer : StrategyOptimizerBase<RsiStrategySettings, RsiOptimizerConfig>
{
    private readonly FitnessFunction _fitnessFunction;

    public RsiStrategyOptimizer(
        RsiOptimizerConfig? config = null,
        RiskSettings? riskSettings = null,
        BacktestSettings? backtestSettings = null,
        FitnessFunction fitnessFunction = FitnessFunction.RiskAdjusted,
        PerformanceFitnessPolicy? policy = null)
        : base(config ?? new RsiOptimizerConfig(), riskSettings, backtestSettings, policy)
    {
        _fitnessFunction = fitnessFunction;
    }

    protected override RsiStrategySettings CreateRandom()
    {
        return new RsiStrategySettings
        {
            RsiPeriod = RandomInt(Config.RsiPeriodMin, Config.RsiPeriodMax),
            OversoldLevel = RandomDecimal(Config.OversoldMin, Config.OversoldMax),
            OverboughtLevel = RandomDecimal(Config.OverboughtMin, Config.OverboughtMax),
            NeutralZoneLow = Config.NeutralZoneLow,
            NeutralZoneHigh = Config.NeutralZoneHigh,
            ExitOnNeutral = Random.NextDouble() > 0.5,
            AtrPeriod = Config.AtrPeriod,
            AtrStopMultiplier = RandomDecimal(Config.AtrMultiplierMin, Config.AtrMultiplierMax),
            TakeProfitMultiplier = RandomDecimal(Config.TakeProfitMultiplierMin, Config.TakeProfitMultiplierMax),
            TrendFilterPeriod = RandomInt(Config.TrendFilterMin, Config.TrendFilterMax),
            UseTrendFilter = Random.NextDouble() > 0.5,
            VolumePeriod = Config.VolumePeriod,
            VolumeThreshold = RandomDecimal(Config.VolumeThresholdMin, Config.VolumeThresholdMax),
            RequireVolumeConfirmation = Random.NextDouble() > 0.5
        };
    }

    protected override RsiStrategySettings Mutate(RsiStrategySettings settings)
    {
        var paramIndex = Random.Next(10);
        return paramIndex switch
        {
            0 => settings with { RsiPeriod = MutateInt(settings.RsiPeriod, Config.RsiPeriodMin, Config.RsiPeriodMax) },
            1 => settings with { OversoldLevel = MutateDecimal(settings.OversoldLevel, Config.OversoldMin, Config.OversoldMax) },
            2 => settings with { OverboughtLevel = MutateDecimal(settings.OverboughtLevel, Config.OverboughtMin, Config.OverboughtMax) },
            3 => settings with { AtrStopMultiplier = MutateDecimal(settings.AtrStopMultiplier, Config.AtrMultiplierMin, Config.AtrMultiplierMax) },
            4 => settings with { TakeProfitMultiplier = MutateDecimal(settings.TakeProfitMultiplier, Config.TakeProfitMultiplierMin, Config.TakeProfitMultiplierMax) },
            5 => settings with { TrendFilterPeriod = MutateInt(settings.TrendFilterPeriod, Config.TrendFilterMin, Config.TrendFilterMax) },
            6 => settings with { VolumeThreshold = MutateDecimal(settings.VolumeThreshold, Config.VolumeThresholdMin, Config.VolumeThresholdMax) },
            7 => settings with { UseTrendFilter = !settings.UseTrendFilter },
            8 => settings with { ExitOnNeutral = !settings.ExitOnNeutral },
            _ => settings with { RequireVolumeConfirmation = !settings.RequireVolumeConfirmation }
        };
    }

    protected override RsiStrategySettings Crossover(RsiStrategySettings parent1, RsiStrategySettings parent2)
    {
        return new RsiStrategySettings
        {
            RsiPeriod = Pick(parent1.RsiPeriod, parent2.RsiPeriod),
            OversoldLevel = Pick(parent1.OversoldLevel, parent2.OversoldLevel),
            OverboughtLevel = Pick(parent1.OverboughtLevel, parent2.OverboughtLevel),
            NeutralZoneLow = Config.NeutralZoneLow,
            NeutralZoneHigh = Config.NeutralZoneHigh,
            ExitOnNeutral = Pick(parent1.ExitOnNeutral, parent2.ExitOnNeutral),
            AtrPeriod = Config.AtrPeriod,
            AtrStopMultiplier = Pick(parent1.AtrStopMultiplier, parent2.AtrStopMultiplier),
            TakeProfitMultiplier = Pick(parent1.TakeProfitMultiplier, parent2.TakeProfitMultiplier),
            TrendFilterPeriod = Pick(parent1.TrendFilterPeriod, parent2.TrendFilterPeriod),
            UseTrendFilter = Pick(parent1.UseTrendFilter, parent2.UseTrendFilter),
            VolumePeriod = Config.VolumePeriod,
            VolumeThreshold = Pick(parent1.VolumeThreshold, parent2.VolumeThreshold),
            RequireVolumeConfirmation = Pick(parent1.RequireVolumeConfirmation, parent2.RequireVolumeConfirmation)
        };
    }

    protected override bool Validate(RsiStrategySettings settings)
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

    protected override decimal EvaluateFitness(RsiStrategySettings settings, List<Candle> candles, string symbol)
    {
        if (!Validate(settings))
            return FitnessCalculator.InvalidSettingsPenalty;

        try
        {
            var strategy = new RsiStrategy(settings);
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
