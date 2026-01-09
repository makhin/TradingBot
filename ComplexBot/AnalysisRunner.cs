using Spectre.Console;
using TradingBot.Core.Models;
using TradingBot.Core.Analytics;
using ComplexBot.Services.Backtesting;
using TradingBot.Core.Utils;

namespace ComplexBot;

class AnalysisRunner
{
    private readonly DataRunner _dataRunner;
    private readonly SettingsService _settingsService;
    private readonly StrategyFactory _strategyFactory;
    private readonly ResultsRenderer _resultsRenderer;

    public AnalysisRunner(
        DataRunner dataRunner,
        SettingsService settingsService,
        StrategyFactory strategyFactory,
        ResultsRenderer resultsRenderer)
    {
        _dataRunner = dataRunner;
        _settingsService = settingsService;
        _strategyFactory = strategyFactory;
        _resultsRenderer = resultsRenderer;
    }

    public async Task RunWalkForward()
    {
        var (candles, symbol) = await _dataRunner.LoadData();
        if (candles.Count == 0) return;

        var riskSettings = _settingsService.GetRiskSettings();
        var strategySettings = _settingsService.GetStrategySettings();
        var backtestSettings = new BacktestSettings { InitialCapital = 10000m };

        var (strategyName, strategyFactory) = _strategyFactory.SelectStrategyWithFactory(strategySettings);

        var wfSettings = GetWalkForwardSettings(candles.Count);
        var analyzer = new WalkForwardAnalyzer(wfSettings);

        WalkForwardResult result = null!;
        await AnsiConsole.Status()
            .StartAsync($"Running walk-forward analysis for {strategyName}...", async ctx =>
            {
                result = analyzer.Analyze(
                    candles,
                    symbol,
                    strategyFactory,
                    riskSettings,
                    backtestSettings
                );
                await Task.CompletedTask;
            });

        _resultsRenderer.DisplayWalkForwardResults(result);
    }

    private WalkForwardSettings GetWalkForwardSettings(int totalCandles)
    {
        var useDefaults = AnsiConsole.Confirm("Use default Walk-Forward settings?", defaultValue: true);

        if (useDefaults)
            return new WalkForwardSettings();

        AnsiConsole.MarkupLine("\n[yellow]Walk-Forward Analysis Settings[/]");
        AnsiConsole.MarkupLine("[grey]Press Enter to keep default value shown in brackets[/]");
        AnsiConsole.MarkupLine($"[grey]Total candles available: {totalCandles}[/]\n");

        var inSampleRatio = SpectreHelpers.AskDecimal("In-Sample ratio (0.0-1.0)", 0.7m, min: 0.3m, max: 0.9m);
        var outOfSampleRatio = SpectreHelpers.AskDecimal("Out-Of-Sample ratio (0.0-1.0)", 0.2m, min: 0.1m, max: 0.5m);
        var stepRatio = SpectreHelpers.AskDecimal("Step ratio (0.0-1.0)", 0.1m, min: 0.01m, max: 0.5m);

        // Validate that IS + OOS doesn't exceed 1.0
        while (inSampleRatio + outOfSampleRatio > 1.0m)
        {
            AnsiConsole.MarkupLine("[red]Error: In-Sample + Out-Of-Sample cannot exceed 1.0[/]");
            inSampleRatio = SpectreHelpers.AskDecimal("In-Sample ratio (0.0-1.0)", 0.7m, min: 0.3m, max: 0.9m);
            outOfSampleRatio = SpectreHelpers.AskDecimal("Out-Of-Sample ratio (0.0-1.0)", 0.2m, min: 0.1m, max: 0.5m);
        }

        // Calculate estimated number of periods
        int windowSize = (int)(totalCandles * inSampleRatio);
        int oosSize = (int)(totalCandles * outOfSampleRatio);
        int stepSize = (int)(totalCandles * stepRatio);
        int estimatedPeriods = 0;
        int startIndex = 0;
        while (startIndex + windowSize + oosSize <= totalCandles)
        {
            estimatedPeriods++;
            startIndex += stepSize;
        }

        AnsiConsole.MarkupLine($"\n[cyan]Estimated periods: {estimatedPeriods}[/]");
        AnsiConsole.MarkupLine($"[grey]Window size: {windowSize} candles (IS) + {oosSize} candles (OOS)[/]");
        AnsiConsole.MarkupLine($"[grey]Step size: {stepSize} candles[/]\n");

        AnsiConsole.MarkupLine("[yellow]Robustness Thresholds[/]");
        var minWfe = SpectreHelpers.AskDecimal("Min WFE % for robust strategy", 50m, min: 0m, max: 100m);
        var minConsistency = SpectreHelpers.AskDecimal("Min consistency % (profitable OOS periods)", 60m, min: 0m, max: 100m);
        var minSharpe = SpectreHelpers.AskDecimal("Min Sharpe ratio", 0.5m, min: 0m, max: 5m);

        return new WalkForwardSettings
        {
            InSampleRatio = inSampleRatio,
            OutOfSampleRatio = outOfSampleRatio,
            StepRatio = stepRatio,
            MinWfeThreshold = minWfe,
            MinConsistencyThreshold = minConsistency,
            MinSharpeThreshold = minSharpe
        };
    }


    public async Task RunMonteCarlo()
    {
        var (candles, symbol) = await _dataRunner.LoadData();
        if (candles.Count == 0) return;

        var riskSettings = _settingsService.GetRiskSettings();
        var strategySettings = _settingsService.GetStrategySettings();
        var backtestSettings = new BacktestSettings { InitialCapital = 10000m };

        var strategy = _strategyFactory.SelectStrategy(strategySettings);
        AnsiConsole.MarkupLine($"\n[yellow]Running Monte Carlo for: {strategy.Name}[/]");

        var engine = new BacktestEngine(strategy, riskSettings, backtestSettings);
        var backtestResult = engine.Run(candles, symbol);

        var simulations = SpectreHelpers.AskInt("Number of simulations", 1000, min: 100, max: 10000);
        var simulator = new MonteCarloSimulator(simulations);

        MonteCarloResult result = null!;
        await AnsiConsole.Status()
            .StartAsync("Running " + simulations + " Monte Carlo simulations...", async ctx =>
            {
                result = simulator.Simulate(backtestResult);
                await Task.CompletedTask;
            });

        _resultsRenderer.DisplayMonteCarloResults(result, backtestResult);
    }
}
