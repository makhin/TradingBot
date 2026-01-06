using System;
using System.Collections.Generic;
using System.Linq;
using ComplexBot.Configuration;
using ComplexBot.Models;

namespace ComplexBot.Services.Strategies;

public enum StrategyOptimizationMode
{
    FullGridSearch,
    Genetic,
    QuickTest,
    EnsembleWeightsOnly,
    EnsembleFull
}

public sealed record StrategyDefinition(
    StrategyKind Kind,
    string Name,
    string ShortName,
    IReadOnlyList<StrategyOptimizationMode> OptimizationModes);

public sealed record StrategyOptimizationOption(
    StrategyKind Kind,
    StrategyOptimizationMode Mode,
    string Label);

public class StrategyRegistry
{
    private static readonly IReadOnlyList<StrategyDefinition> Definitions =
    [
        new StrategyDefinition(
            StrategyKind.AdxTrendFollowing,
            "ADX Trend Following",
            "ADX",
            [StrategyOptimizationMode.FullGridSearch]),
        new StrategyDefinition(
            StrategyKind.RsiMeanReversion,
            "RSI Mean Reversion",
            "RSI",
            [StrategyOptimizationMode.Genetic, StrategyOptimizationMode.QuickTest]),
        new StrategyDefinition(
            StrategyKind.MaCrossover,
            "MA Crossover",
            "MA",
            [StrategyOptimizationMode.Genetic, StrategyOptimizationMode.QuickTest]),
        new StrategyDefinition(
            StrategyKind.StrategyEnsemble,
            "Strategy Ensemble",
            "Ensemble",
            [StrategyOptimizationMode.EnsembleWeightsOnly, StrategyOptimizationMode.EnsembleFull])
    ];

    private static readonly IReadOnlyDictionary<StrategyKind, StrategyDefinition> DefinitionMap =
        Definitions.ToDictionary(definition => definition.Kind);

    private static readonly IReadOnlyDictionary<StrategyOptimizationMode, string> OptimizationModeLabels =
        new Dictionary<StrategyOptimizationMode, string>
        {
            [StrategyOptimizationMode.FullGridSearch] = "Full Grid Search",
            [StrategyOptimizationMode.Genetic] = "Genetic",
            [StrategyOptimizationMode.QuickTest] = "Quick Test",
            [StrategyOptimizationMode.EnsembleWeightsOnly] = "Weights Only",
            [StrategyOptimizationMode.EnsembleFull] = "Full - All Parameters"
        };

    private static readonly IReadOnlyList<StrategyOptimizationOption> OptimizationOptionList =
        BuildOptimizationOptions();

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

    public static IReadOnlyList<StrategyOptimizationOption> OptimizationOptions => OptimizationOptionList;

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

    private static IReadOnlyList<StrategyOptimizationOption> BuildOptimizationOptions()
    {
        var options = new List<StrategyOptimizationOption>();
        foreach (var definition in Definitions)
        {
            foreach (var mode in definition.OptimizationModes)
            {
                var label = $"{definition.Name} ({OptimizationModeLabels[mode]})";
                options.Add(new StrategyOptimizationOption(definition.Kind, mode, label));
            }
        }

        return options;
    }
}
