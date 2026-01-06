using Spectre.Console;
using ComplexBot.Configuration;
using ComplexBot.Models;
using ComplexBot.Services.Analytics;
using ComplexBot.Services.Backtesting;
using ComplexBot.Services.Strategies;
using ComplexBot.Utils;

namespace ComplexBot;

class BacktestRunner
{
    private readonly DataRunner _dataRunner;
    private readonly SettingsService _settingsService;
    private readonly ConfigurationService _configService;
    private readonly StrategyFactory _strategyFactory;
    private readonly ResultsRenderer _resultsRenderer;

    public BacktestRunner(
        DataRunner dataRunner,
        SettingsService settingsService,
        ConfigurationService configService,
        StrategyFactory strategyFactory,
        ResultsRenderer resultsRenderer)
    {
        _dataRunner = dataRunner;
        _settingsService = settingsService;
        _configService = configService;
        _strategyFactory = strategyFactory;
        _resultsRenderer = resultsRenderer;
    }

    public async Task RunBacktest()
    {
        var (candles, symbol) = await _dataRunner.LoadData();
        if (candles.Count == 0) return;

        var riskSettings = _settingsService.GetRiskSettings();
        var strategySettings = _settingsService.GetStrategySettings();
        var backtestSettings = _configService.GetConfiguration().Backtest.ToBacktestSettings();
        backtestSettings = backtestSettings with
        {
            InitialCapital = SpectreHelpers.AskDecimal(
                "Initial capital [green](USDT)[/]",
                backtestSettings.InitialCapital,
                min: 1m)
        };

        var strategy = _strategyFactory.SelectStrategy(strategySettings);
        var journal = new TradeJournal();
        var engine = new BacktestEngine(strategy, riskSettings, backtestSettings, journal);

        BacktestResult result = null!;
        await AnsiConsole.Status()
            .StartAsync("Running backtest...", async ctx =>
            {
                result = engine.Run(candles, symbol);
                await Task.CompletedTask;
            });

        _resultsRenderer.DisplayBacktestResults(result);

        if (AnsiConsole.Confirm("Export trade journal to CSV?", defaultValue: true))
        {
            journal.ExportToCsv();
            var stats = journal.GetStats();
            AnsiConsole.MarkupLine($"\n[green]Trade Journal Statistics:[/]");
            AnsiConsole.MarkupLine($"  Total Trades: {stats.TotalTrades}");
            AnsiConsole.MarkupLine($"  Win Rate: {stats.WinRate:F1}%");
            AnsiConsole.MarkupLine($"  Average R-Multiple: {stats.AverageRMultiple:F2}");
            AnsiConsole.MarkupLine($"  Total Net P&L: ${stats.TotalNetPnL:F2}");
            AnsiConsole.MarkupLine($"  Average Win: ${stats.AverageWin:F2}");
            AnsiConsole.MarkupLine($"  Average Loss: ${stats.AverageLoss:F2}");
            AnsiConsole.MarkupLine($"  Largest Win: ${stats.LargestWin:F2}");
            AnsiConsole.MarkupLine($"  Largest Loss: ${stats.LargestLoss:F2}");
            AnsiConsole.MarkupLine($"  Average Bars in Trade: {stats.AverageBarsInTrade:F1}");
        }
    }
}
