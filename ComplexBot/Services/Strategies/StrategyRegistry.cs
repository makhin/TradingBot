using System;
using System.Collections.Generic;
using System.Linq;
using ComplexBot.Configuration;
using ComplexBot.Models;

namespace ComplexBot.Services.Strategies;

public sealed record StrategyDefinition(
    StrategyKind Kind,
    string Name,
    string ShortName,
    IReadOnlyList<OptimizationMode> OptimizationModes);

public class StrategyRegistry
{
    private static readonly IReadOnlyList<StrategyDefinition> Definitions =
    [
        new StrategyDefinition(
            StrategyKind.AdxTrendFollowing,
            "ADX Trend Following",
            "ADX",
            [OptimizationMode.Full, OptimizationMode.Genetic]),
        new StrategyDefinition(
            StrategyKind.RsiMeanReversion,
            "RSI Mean Reversion",
            "RSI",
            [OptimizationMode.Genetic, OptimizationMode.Quick]),
        new StrategyDefinition(
            StrategyKind.MaCrossover,
            "MA Crossover",
            "MA",
            [OptimizationMode.Genetic, OptimizationMode.Quick]),
        new StrategyDefinition(
            StrategyKind.StrategyEnsemble,
            "Strategy Ensemble",
            "Ensemble",
            [OptimizationMode.EnsembleWeightsOnly, OptimizationMode.EnsembleFull])
    ];

    private static readonly IReadOnlyDictionary<StrategyKind, StrategyDefinition> DefinitionMap =
        Definitions.ToDictionary(definition => definition.Kind);

    private static readonly IReadOnlyList<OptimizationScenario> OptimizationScenarioList =
        BuildOptimizationScenarios();

    private static readonly IReadOnlyList<StrategyKind> StrategyKindOrder =
        Definitions.Select(definition => definition.Kind).ToList();

    private readonly ConfigurationService _configService;
    private readonly IReadOnlyDictionary<StrategyKind, Func<StrategySettings?, IStrategy>> _strategyFactories;
    private readonly IReadOnlyDictionary<StrategyKind, Func<BotConfiguration, object>> _configFactories;

    public StrategyRegistry(ConfigurationService configService)
    {
        _configService = configService;
        _configFactories = new Dictionary<StrategyKind, Func<BotConfiguration, object>>
        {
            [StrategyKind.AdxTrendFollowing] = config => config.Strategy.ToStrategySettings(),
            [StrategyKind.RsiMeanReversion] = config => config.RsiStrategy.ToRsiStrategySettings(),
            [StrategyKind.MaCrossover] = config => config.MaStrategy.ToMaStrategySettings(),
            [StrategyKind.StrategyEnsemble] = config => config.Ensemble.ToEnsembleSettings()
        };

        _strategyFactories = new Dictionary<StrategyKind, Func<StrategySettings?, IStrategy>>
        {
            [StrategyKind.AdxTrendFollowing] = adxSettings =>
                new AdxTrendStrategy(adxSettings ?? GetSettings<StrategySettings>(StrategyKind.AdxTrendFollowing)),
            [StrategyKind.RsiMeanReversion] = _ =>
                new RsiStrategy(GetSettings<RsiStrategySettings>(StrategyKind.RsiMeanReversion)),
            [StrategyKind.MaCrossover] = _ =>
                new MaStrategy(GetSettings<MaStrategySettings>(StrategyKind.MaCrossover)),
            [StrategyKind.StrategyEnsemble] = _ =>
                StrategyEnsemble.CreateDefault(GetSettings<EnsembleSettings>(StrategyKind.StrategyEnsemble))
        };
    }

    public static IReadOnlyList<StrategyKind> StrategyOrder => StrategyKindOrder;

    public static IReadOnlyList<OptimizationScenario> OptimizationScenarios => OptimizationScenarioList;

    public static StrategyDefinition GetDefinition(StrategyKind kind) => DefinitionMap[kind];

    public static string GetStrategyLabel(StrategyKind kind) => DefinitionMap[kind].Name;

    public static string GetStrategyShortName(StrategyKind kind) => DefinitionMap[kind].ShortName;

    public IStrategy CreateStrategy(StrategyKind kind, StrategySettings? adxSettings = null)
    {
        if (!_strategyFactories.TryGetValue(kind, out var factory))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported strategy kind");

        return factory(adxSettings);
    }

    public Func<IStrategy> GetStrategyFactory(StrategyKind kind, StrategySettings? adxSettings = null)
    {
        if (!_strategyFactories.TryGetValue(kind, out var factory))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported strategy kind");

        return () => factory(adxSettings);
    }

    public TSettings GetSettings<TSettings>(StrategyKind kind)
        where TSettings : class
    {
        if (!_configFactories.TryGetValue(kind, out var factory))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported strategy kind");

        var settings = factory(_configService.GetConfiguration());
        if (settings is not TSettings typed)
            throw new InvalidOperationException($"Settings for {kind} are not of type {typeof(TSettings).Name}.");

        return typed;
    }

    private static IReadOnlyList<OptimizationScenario> BuildOptimizationScenarios()
    {
        var options = new List<OptimizationScenario>();
        foreach (var definition in Definitions)
        {
            foreach (var mode in definition.OptimizationModes)
            {
                options.Add(new OptimizationScenario(definition.Kind, mode));
            }
        }

        return options;
    }
}
