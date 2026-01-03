using Spectre.Console;
using ComplexBot.Models;
using ComplexBot.Services.Analytics;
using ComplexBot.Services.Backtesting;

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

        var inSampleRatio = PromptDecimal("In-Sample ratio (0.0-1.0)", 0.7m, 0.3m, 0.9m);
        var outOfSampleRatio = PromptDecimal("Out-Of-Sample ratio (0.0-1.0)", 0.2m, 0.1m, 0.5m);
        var stepRatio = PromptDecimal("Step ratio (0.0-1.0)", 0.1m, 0.01m, 0.5m);

        // Validate that IS + OOS doesn't exceed 1.0
        while (inSampleRatio + outOfSampleRatio > 1.0m)
        {
            AnsiConsole.MarkupLine("[red]Error: In-Sample + Out-Of-Sample cannot exceed 1.0[/]");
            inSampleRatio = PromptDecimal("In-Sample ratio (0.0-1.0)", 0.7m, 0.3m, 0.9m);
            outOfSampleRatio = PromptDecimal("Out-Of-Sample ratio (0.0-1.0)", 0.2m, 0.1m, 0.5m);
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
        var minWfe = PromptDecimal("Min WFE % for robust strategy", 50m, 0m, 100m);
        var minConsistency = PromptDecimal("Min consistency % (profitable OOS periods)", 60m, 0m, 100m);
        var minSharpe = PromptDecimal("Min Sharpe ratio", 0.5m, 0m, 5m);

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

    private decimal PromptDecimal(string prompt, decimal defaultValue, decimal min, decimal max)
    {
        while (true)
        {
            // Use [[ ]] to escape brackets in Spectre.Console markup
            var formattedDefault = defaultValue.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            var input = AnsiConsole.Ask($"{prompt} [[{formattedDefault}]]:", defaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (string.IsNullOrWhiteSpace(input))
                return defaultValue;

            if (decimal.TryParse(input.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                if (value >= min && value <= max)
                    return value;

                AnsiConsole.MarkupLine($"[red]Value must be between {min} and {max}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Invalid number format[/]");
            }
        }
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

        int simulations = AnsiConsole.Ask("Number of simulations:", 1000);
        var simulator = new MonteCarloSimulator(simulations);

        MonteCarloResult result = null!;
        await AnsiConsole.Status()
            .StartAsync($"Running {simulations} Monte Carlo simulations...", async ctx =>
            {
                result = simulator.Simulate(backtestResult);
                await Task.CompletedTask;
            });

        _resultsRenderer.DisplayMonteCarloResults(result, backtestResult);
    }
}
