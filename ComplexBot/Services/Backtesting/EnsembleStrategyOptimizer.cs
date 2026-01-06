using ComplexBot.Models;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Strategies;

namespace ComplexBot.Services.Backtesting;

public class EnsembleStrategyOptimizer : StrategyOptimizerBase<EnsembleOptimizationSettings, EnsembleOptimizerConfig>
{
    private readonly OptimizationTarget _optimizeFor;

    public EnsembleStrategyOptimizer(
        EnsembleOptimizerConfig? config = null,
        RiskSettings? riskSettings = null,
        BacktestSettings? backtestSettings = null,
        OptimizationTarget optimizeFor = OptimizationTarget.RiskAdjusted,
        PerformanceFitnessPolicy? policy = null)
        : this(config ?? new EnsembleOptimizerConfig(), riskSettings, backtestSettings, optimizeFor, policy)
    {
    }

    private EnsembleStrategyOptimizer(
        EnsembleOptimizerConfig config,
        RiskSettings? riskSettings,
        BacktestSettings? backtestSettings,
        OptimizationTarget optimizeFor,
        PerformanceFitnessPolicy? policy)
        : base(config, riskSettings, backtestSettings, policy ?? new PerformanceFitnessPolicy { MinTrades = config.MinTrades })
    {
        _optimizeFor = optimizeFor;
    }

    protected override EnsembleOptimizationSettings CreateRandom()
    {
        return new EnsembleOptimizationSettings
        {
            AdxWeight = RandomDecimal(Config.WeightMin, Config.WeightMax),
            MaWeight = RandomDecimal(Config.WeightMin, Config.WeightMax),
            RsiWeight = RandomDecimal(Config.WeightMin, Config.WeightMax),
            MinimumAgreement = RandomDecimal(Config.MinimumAgreementMin, Config.MinimumAgreementMax),
            UseConfidenceWeighting = Config.AllowConfidenceWeightingToggle
                ? Random.NextDouble() > 0.5
                : Config.DefaultUseConfidenceWeighting
        };
    }

    protected override EnsembleOptimizationSettings Mutate(EnsembleOptimizationSettings settings)
    {
        var paramCount = Config.AllowConfidenceWeightingToggle ? 5 : 4;
        var paramIndex = Random.Next(paramCount);

        return paramIndex switch
        {
            0 => settings with { AdxWeight = MutateDecimal(settings.AdxWeight, Config.WeightMin, Config.WeightMax) },
            1 => settings with { MaWeight = MutateDecimal(settings.MaWeight, Config.WeightMin, Config.WeightMax) },
            2 => settings with { RsiWeight = MutateDecimal(settings.RsiWeight, Config.WeightMin, Config.WeightMax) },
            3 => settings with { MinimumAgreement = MutateDecimal(settings.MinimumAgreement, Config.MinimumAgreementMin, Config.MinimumAgreementMax) },
            _ => settings with { UseConfidenceWeighting = !settings.UseConfidenceWeighting }
        };
    }

    protected override EnsembleOptimizationSettings Crossover(
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

    protected override bool Validate(EnsembleOptimizationSettings settings)
    {
        if (!IsInRange(settings.AdxWeight, Config.WeightMin, Config.WeightMax))
            return false;
        if (!IsInRange(settings.MaWeight, Config.WeightMin, Config.WeightMax))
            return false;
        if (!IsInRange(settings.RsiWeight, Config.WeightMin, Config.WeightMax))
            return false;
        if (!IsInRange(settings.MinimumAgreement, Config.MinimumAgreementMin, Config.MinimumAgreementMax))
            return false;

        return true;
    }

    protected override decimal EvaluateFitness(
        EnsembleOptimizationSettings settings,
        List<Candle> candles,
        string symbol)
    {
        if (!Validate(settings))
            return FitnessCalculator.InvalidSettingsPenalty;

        try
        {
            var strategy = StrategyEnsemble.CreateDefault(settings.ToEnsembleSettings());
            var engine = new BacktestEngine(strategy, RiskSettings, BacktestSettings);
            var result = engine.Run(candles, symbol);

            return FitnessCalculator.CalculateScore(_optimizeFor, result.Metrics);
        }
        catch
        {
            return FitnessCalculator.InvalidSettingsPenalty;
        }
    }
}
