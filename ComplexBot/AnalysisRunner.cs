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

        var analyzer = new WalkForwardAnalyzer();

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
