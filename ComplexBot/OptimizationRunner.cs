using Spectre.Console;
using System.Linq;
using ComplexBot.Configuration;
using ComplexBot.Models;
using ComplexBot.Services.Analytics;
using ComplexBot.Services.Backtesting;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Strategies;
using ComplexBot.Utils;

namespace ComplexBot;

class OptimizationRunner
{
    private readonly DataRunner _dataRunner;
    private readonly SettingsService _settingsService;
    private readonly ResultsRenderer _resultsRenderer;
    private readonly ConfigurationService _configService;
    private readonly StrategyRegistry _strategyRegistry;

    public OptimizationRunner(
        DataRunner dataRunner,
        SettingsService settingsService,
        ResultsRenderer resultsRenderer,
        ConfigurationService configService,
        StrategyRegistry strategyRegistry)
    {
        _dataRunner = dataRunner;
        _settingsService = settingsService;
        _resultsRenderer = resultsRenderer;
        _configService = configService;
        _strategyRegistry = strategyRegistry;
    }

    public async Task RunOptimization()
    {
        var (candles, symbol) = await _dataRunner.LoadData();
        if (candles.Count == 0) return;

        var scenario = AnsiConsole.Prompt(
            new SelectionPrompt<OptimizationScenario>()
                .Title("Select strategy to optimize:")
                .UseConverter(UiMappings.GetOptimizationScenarioLabel)
                .AddChoices(StrategyRegistry.OptimizationScenarios)
        );

        var riskSettings = _settingsService.GetRiskSettings();
        var backtestSettings = _configService.GetConfiguration().Backtest.ToBacktestSettings();

        switch (scenario)
        {
            case { Kind: StrategyKind.StrategyEnsemble, Mode: OptimizationMode.EnsembleWeightsOnly }:
            {
                var optimizeFor = PromptOptimizationTarget();
                await RunEnsembleOptimization(candles, symbol, riskSettings, backtestSettings, optimizeFor);
                return;
            }
            case { Kind: StrategyKind.StrategyEnsemble, Mode: OptimizationMode.EnsembleFull }:
            {
                var optimizeFor = PromptOptimizationTarget();
                await RunFullEnsembleOptimization(candles, symbol, riskSettings, backtestSettings, optimizeFor);
                return;
            }
            case { Kind: StrategyKind.MaCrossover, Mode: OptimizationMode.Genetic }:
            {
                var optimizeFor = PromptOptimizationTarget();
                await RunMaOptimization(candles, symbol, riskSettings, backtestSettings, optimizeFor);
                return;
            }
            case { Kind: StrategyKind.RsiMeanReversion, Mode: OptimizationMode.Genetic }:
            {
                var optimizeFor = PromptOptimizationTarget();
                await RunRsiOptimization(candles, symbol, riskSettings, backtestSettings, optimizeFor);
                return;
            }
            case { Mode: OptimizationMode.Quick }:
            {
                var strategy = _strategyRegistry.CreateStrategy(scenario.Kind);
                AnsiConsole.MarkupLine($"\n[yellow]Running backtest for {strategy.Name}...[/]");
                var journal = new TradeJournal();
                var engine = new BacktestEngine(strategy, riskSettings, backtestSettings, journal);
                var btResult = engine.Run(candles, symbol);
                _resultsRenderer.DisplayBacktestResults(btResult);
                return;
            }
            case { Mode: OptimizationMode.Full }:
                break;
            default:
                throw new InvalidOperationException($"Unsupported optimization scenario: {scenario}");
        }

        AnsiConsole.MarkupLine("\n[yellow]ADX Parameter Ranges (Grid Search)[/]");

        var adxOptimizeFor = PromptOptimizationTarget();

        var useDefaultRanges = AnsiConsole.Confirm("Use default parameter ranges?", true);

        OptimizerSettings optimizerSettings;
        if (useDefaultRanges)
        {
            optimizerSettings = new OptimizerSettings { OptimizeFor = adxOptimizeFor };
        }
        else
        {
            optimizerSettings = new OptimizerSettings
            {
                OptimizeFor = adxOptimizeFor,
                AdxPeriodRange = ParseIntRange(AnsiConsole.Ask("ADX periods [green](comma-separated)[/]:", "10,14,20")),
                AdxThresholdRange = ParseDecimalRange(AnsiConsole.Ask("ADX thresholds:", "20,25,30")),
                FastEmaRange = ParseIntRange(AnsiConsole.Ask("Fast EMA periods:", "10,15,20,25")),
                SlowEmaRange = ParseIntRange(AnsiConsole.Ask("Slow EMA periods:", "40,50,60,80")),
                AtrMultiplierRange = ParseDecimalRange(AnsiConsole.Ask("ATR multipliers:", "2.0,2.5,3.0")),
                VolumeThresholdRange = ParseDecimalRange(AnsiConsole.Ask("Volume thresholds:", "1.0,1.5,2.0"))
            };
        }

        var optimizer = new ParameterOptimizer(optimizerSettings);

        int totalCombos = optimizerSettings.AdxPeriodRange.Length
            * optimizerSettings.AdxThresholdRange.Length
            * optimizerSettings.FastEmaRange.Length
            * optimizerSettings.SlowEmaRange.Length
            * optimizerSettings.AtrMultiplierRange.Length
            * optimizerSettings.VolumeThresholdRange.Length;

        AnsiConsole.MarkupLine($"\n[grey]Testing ~{totalCombos} parameter combinations...[/]\n");

        OptimizationResult result = null!;
        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Optimizing parameters...");
                var progress = new Progress<OptimizationProgress>(p =>
                {
                    task.Value = p.PercentComplete;
                });

                result = optimizer.Optimize(candles, symbol, riskSettings, backtestSettings, progress);
                await Task.CompletedTask;
            });

        _resultsRenderer.DisplayOptimizationResults(result);
        PromptSaveAdxSettings(result);
    }

    private void PromptSaveAdxSettings(OptimizationResult result)
    {
        if (result.BestRobustParameters == null && result.BestInSampleParameters == null)
            return;

        var best = result.BestRobustParameters ?? result.BestInSampleParameters;
        if (best == null) return;

        var useRobust = result.BestRobustParameters != null;
        AnsiConsole.MarkupLine(useRobust
            ? "[green]Using best ROBUST parameters (validated on out-of-sample data)[/]"
            : "[yellow]Warning: No robust parameters found, using best in-sample parameters[/]");

        if (!AnsiConsole.Confirm("Save best ADX parameters to appsettings.user.json?", defaultValue: true))
            return;

        var strategySettings = new StrategyConfigSettings
        {
            AdxPeriod = best.AdxPeriod,
            AdxThreshold = best.AdxThreshold,
            AdxExitThreshold = best.AdxExitThreshold,
            FastEmaPeriod = best.FastEmaPeriod,
            SlowEmaPeriod = best.SlowEmaPeriod,
            AtrStopMultiplier = best.AtrStopMultiplier,
            VolumeThreshold = best.VolumeThreshold,
            RequireVolumeConfirmation = best.RequireVolumeConfirmation
        };

        _configService.UpdateSection("Strategy", strategySettings);
        AnsiConsole.MarkupLine("[green]✓ ADX settings saved to appsettings.user.json[/]");
    }

    private static int[] ParseIntRange(string input) =>
        input.Split(',').Select(s => int.Parse(s.Trim())).ToArray();

    private static decimal[] ParseDecimalRange(string input) =>
        input.Split(',').Select(s => decimal.Parse(s.Trim())).ToArray();

    private static OptimizationTarget PromptOptimizationTarget() =>
        AnsiConsole.Prompt(
            new SelectionPrompt<OptimizationTarget>()
                .Title("Optimize for:")
                .AddChoices(
                    OptimizationTarget.RiskAdjusted,
                    OptimizationTarget.SharpeRatio,
                    OptimizationTarget.SortinoRatio,
                    OptimizationTarget.ProfitFactor,
                    OptimizationTarget.TotalReturn
                )
        );

    private static FitnessFunction ToFitnessFunction(OptimizationTarget target) => target switch
    {
        OptimizationTarget.SharpeRatio => FitnessFunction.Sharpe,
        OptimizationTarget.SortinoRatio => FitnessFunction.Sortino,
        OptimizationTarget.ProfitFactor => FitnessFunction.ProfitFactor,
        OptimizationTarget.TotalReturn => FitnessFunction.Return,
        OptimizationTarget.RiskAdjusted => FitnessFunction.RiskAdjusted,
        _ => FitnessFunction.RiskAdjusted
    };

    private async Task RunEnsembleOptimization(
        List<Candle> candles,
        string symbol,
        RiskSettings riskSettings,
        BacktestSettings backtestSettings,
        OptimizationTarget optimizeFor)
    {
        var config = _configService.GetConfiguration();
        var optimizerConfig = config.EnsembleOptimizer.ToEnsembleOptimizerConfig();
        var geneticSettings = config.GeneticOptimizer.ToGeneticOptimizerSettings();
        var optimizer = new EnsembleStrategyOptimizer(optimizerConfig, riskSettings, backtestSettings, optimizeFor);

        var result = await RunGeneticOptimization<EnsembleOptimizationSettings>(
            "Optimizing ensemble...",
            progress => optimizer.Optimize(candles, symbol, geneticSettings, progress),
            p => $"Optimizing ensemble... Gen {p.CurrentGeneration}/{p.TotalGenerations}");

        var bestSettings = result.BestSettings;
        var ensembleSettings = bestSettings.ToEnsembleSettings();
        var strategy = StrategyEnsemble.CreateDefault(ensembleSettings);
        var engine = new BacktestEngine(strategy, riskSettings, backtestSettings);
        var backtestResult = engine.Run(candles, symbol);

        _resultsRenderer.DisplayEnsembleOptimizationResults(result, backtestResult);
        PromptSaveEnsembleSettings(bestSettings);
    }

    private async Task RunFullEnsembleOptimization(
        List<Candle> candles,
        string symbol,
        RiskSettings riskSettings,
        BacktestSettings backtestSettings,
        OptimizationTarget optimizeFor)
    {
        var config = _configService.GetConfiguration();
        var geneticSettings = config.GeneticOptimizer.ToGeneticOptimizerSettings();

        geneticSettings = geneticSettings with
        {
            PopulationSize = Math.Max(geneticSettings.PopulationSize, 150),
            Generations = Math.Max(geneticSettings.Generations, 80)
        };

        var optimizer = new FullEnsembleOptimizer(
            riskSettings: riskSettings,
            backtestSettings: backtestSettings,
            optimizeFor: optimizeFor);

        AnsiConsole.MarkupLine($"\n[yellow]Full Ensemble Optimization (~25 parameters)[/]");
        AnsiConsole.MarkupLine($"[grey]Population: {geneticSettings.PopulationSize}, Generations: {geneticSettings.Generations}[/]");
        AnsiConsole.MarkupLine("[grey]This may take a while...[/]\n");

        var result = await RunGeneticOptimization<FullEnsembleSettings>(
            "Optimizing full ensemble...",
            progress => optimizer.Optimize(candles, symbol, geneticSettings, progress),
            p => $"Optimizing full ensemble... Gen {p.CurrentGeneration}/{p.TotalGenerations} (Best: {p.BestFitness:F2})");

        var bestSettings = result.BestSettings;
        var strategy = FullEnsembleOptimizer.CreateEnsembleFromSettings(bestSettings);
        var engine = new BacktestEngine(strategy, riskSettings, backtestSettings);
        var backtestResult = engine.Run(candles, symbol);

        _resultsRenderer.DisplayFullEnsembleResults(result, backtestResult);
        PromptSaveFullEnsembleSettings(bestSettings);
    }

    private void PromptSaveFullEnsembleSettings(FullEnsembleSettings settings)
    {
        if (!AnsiConsole.Confirm("Save optimized parameters to appsettings.user.json?", defaultValue: true))
            return;

        var strategySettings = new StrategyConfigSettings
        {
            AdxPeriod = settings.AdxPeriod,
            AdxThreshold = settings.AdxThreshold,
            AdxExitThreshold = settings.AdxExitThreshold,
            FastEmaPeriod = settings.AdxFastEmaPeriod,
            SlowEmaPeriod = settings.AdxSlowEmaPeriod,
            AtrStopMultiplier = settings.AdxAtrStopMultiplier,
            VolumeThreshold = settings.AdxVolumeThreshold,
            RequireVolumeConfirmation = settings.AdxVolumeThreshold > 1.0m
        };
        _configService.UpdateSection("Strategy", strategySettings);

        var maSettings = new MaStrategyConfigSettings
        {
            FastMaPeriod = settings.MaFastPeriod,
            SlowMaPeriod = settings.MaSlowPeriod,
            AtrStopMultiplier = settings.MaAtrStopMultiplier,
            TakeProfitMultiplier = settings.MaTakeProfitMultiplier,
            VolumeThreshold = settings.MaVolumeThreshold,
            RequireVolumeConfirmation = settings.MaVolumeThreshold > 1.0m
        };
        _configService.UpdateSection("MaStrategy", maSettings);

        var rsiSettings = new RsiStrategyConfigSettings
        {
            RsiPeriod = settings.RsiPeriod,
            OversoldLevel = settings.RsiOversoldLevel,
            OverboughtLevel = settings.RsiOverboughtLevel,
            AtrStopMultiplier = settings.RsiAtrStopMultiplier,
            TakeProfitMultiplier = settings.RsiTakeProfitMultiplier,
            UseTrendFilter = settings.RsiUseTrendFilter
        };
        _configService.UpdateSection("RsiStrategy", rsiSettings);

        var ensembleSettings = new EnsembleConfigSettings
        {
            Enabled = true,
            MinimumAgreement = settings.MinimumAgreement,
            UseConfidenceWeighting = settings.UseConfidenceWeighting,
            StrategyWeights = new Dictionary<StrategyKind, decimal>
            {
                [StrategyKind.AdxTrendFollowing] = settings.AdxWeight,
                [StrategyKind.MaCrossover] = settings.MaWeight,
                [StrategyKind.RsiMeanReversion] = settings.RsiWeight
            }
        };
        _configService.UpdateSection("Ensemble", ensembleSettings);

        AnsiConsole.MarkupLine("[green]✓ All settings saved to appsettings.user.json[/]");
    }

    private async Task RunMaOptimization(
        List<Candle> candles,
        string symbol,
        RiskSettings riskSettings,
        BacktestSettings backtestSettings,
        OptimizationTarget optimizeFor)
    {
        var config = _configService.GetConfiguration();
        var optimizerConfig = config.MaOptimizer.ToMaOptimizerConfig();
        var geneticSettings = config.GeneticOptimizer.ToGeneticOptimizerSettings();
        var fitness = ToFitnessFunction(optimizeFor);
        var optimizer = new MaStrategyOptimizer(optimizerConfig, riskSettings, backtestSettings, fitness);

        var result = await RunGeneticOptimization<MaStrategySettings>(
            "Optimizing MA...",
            progress => optimizer.Optimize(candles, symbol, geneticSettings, progress),
            p => $"Optimizing MA... Gen {p.CurrentGeneration}/{p.TotalGenerations}");

        var bestSettings = result.BestSettings;
        var strategy = new MaStrategy(bestSettings);
        var engine = new BacktestEngine(strategy, riskSettings, backtestSettings);
        var backtestResult = engine.Run(candles, symbol);

        _resultsRenderer.DisplayMaOptimizationResults(result, backtestResult);
        PromptSaveMaSettings(bestSettings);
    }

    private async Task RunRsiOptimization(
        List<Candle> candles,
        string symbol,
        RiskSettings riskSettings,
        BacktestSettings backtestSettings,
        OptimizationTarget optimizeFor)
    {
        var config = _configService.GetConfiguration();
        var optimizerConfig = config.RsiOptimizer.ToRsiOptimizerConfig();
        var geneticSettings = config.GeneticOptimizer.ToGeneticOptimizerSettings();
        var fitness = ToFitnessFunction(optimizeFor);
        var optimizer = new RsiStrategyOptimizer(optimizerConfig, riskSettings, backtestSettings, fitness);

        var result = await RunGeneticOptimization<RsiStrategySettings>(
            "Optimizing RSI...",
            progress => optimizer.Optimize(candles, symbol, geneticSettings, progress),
            p => $"Optimizing RSI... Gen {p.CurrentGeneration}/{p.TotalGenerations}");

        var bestSettings = result.BestSettings;
        var strategy = new RsiStrategy(bestSettings);
        var engine = new BacktestEngine(strategy, riskSettings, backtestSettings);
        var backtestResult = engine.Run(candles, symbol);

        _resultsRenderer.DisplayRsiOptimizationResults(result, backtestResult);
        PromptSaveRsiSettings(bestSettings);
    }

    private void PromptSaveEnsembleSettings(EnsembleOptimizationSettings settings)
    {
        if (!AnsiConsole.Confirm("Save best ensemble settings to appsettings.user.json?", defaultValue: true))
            return;

        var current = _configService.GetConfiguration().Ensemble;
        var updated = new EnsembleConfigSettings
        {
            Enabled = current.Enabled,
            MinimumAgreement = settings.MinimumAgreement,
            UseConfidenceWeighting = settings.UseConfidenceWeighting,
            StrategyWeights = new Dictionary<StrategyKind, decimal>
            {
                [StrategyKind.AdxTrendFollowing] = settings.AdxWeight,
                [StrategyKind.MaCrossover] = settings.MaWeight,
                [StrategyKind.RsiMeanReversion] = settings.RsiWeight
            }
        };

        _configService.UpdateSection("Ensemble", updated);
    }

    private void PromptSaveMaSettings(MaStrategySettings settings)
    {
        if (!AnsiConsole.Confirm("Save best MA settings to appsettings.user.json?", defaultValue: true))
            return;

        var updated = MaStrategyConfigSettings.FromSettings(settings);
        _configService.UpdateSection("MaStrategy", updated);
    }

    private void PromptSaveRsiSettings(RsiStrategySettings settings)
    {
        if (!AnsiConsole.Confirm("Save best RSI settings to appsettings.user.json?", defaultValue: true))
            return;

        var updated = RsiStrategyConfigSettings.FromSettings(settings);
        _configService.UpdateSection("RsiStrategy", updated);
    }

    private static async Task<GeneticOptimizationResult<TSettings>> RunGeneticOptimization<TSettings>(
        string taskTitle,
        Func<IProgress<GeneticProgress<TSettings>>, GeneticOptimizationResult<TSettings>> optimize,
        Func<GeneticProgress<TSettings>, string> descriptionFormatter)
        where TSettings : class
    {
        GeneticOptimizationResult<TSettings> result = null!;

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask(taskTitle);
                var progress = new Progress<GeneticProgress<TSettings>>(p =>
                {
                    task.Description = descriptionFormatter(p);
                    task.Value = p.PercentComplete;
                });

                result = optimize(progress);
                await Task.CompletedTask;
            });

        return result;
    }
}
