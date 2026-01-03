using ComplexBot.Configuration;
using Spectre.Console;

namespace ComplexBot;

class Program
{
    static async Task Main(string[] args)
    {
        var configService = new ConfigurationService();

        var menu = new AppMenu();
        var settingsService = new SettingsService(configService);
        var strategyFactory = new StrategyFactory(configService);
        var resultsRenderer = new ResultsRenderer();
        var dataRunner = new DataRunner();

        var backtestRunner = new BacktestRunner(dataRunner, settingsService, strategyFactory, resultsRenderer);
        var optimizationRunner = new OptimizationRunner(dataRunner, settingsService, resultsRenderer, configService);
        var analysisRunner = new AnalysisRunner(dataRunner, settingsService, strategyFactory, resultsRenderer);
        var liveTradingRunner = new LiveTradingRunner(configService, settingsService);

        var dispatcher = new ModeDispatcher(
            backtestRunner,
            optimizationRunner,
            analysisRunner,
            liveTradingRunner,
            dataRunner,
            settingsService,
            configService);

        // Check for TRADING_MODE environment variable for non-interactive Docker execution
        var tradingMode = Environment.GetEnvironmentVariable("TRADING_MODE");
        string mode;

        if (!string.IsNullOrEmpty(tradingMode))
        {
            // Map environment variable to menu option
            mode = tradingMode.ToLowerInvariant() switch
            {
                "live" => "Live Trading (Paper)",
                "live-real" => "Live Trading (Real)",
                "backtest" => "Backtest",
                "optimize" => "Parameter Optimization",
                "walkforward" => "Walk-Forward Analysis",
                "montecarlo" => "Monte Carlo Simulation",
                "download" => "Download Data",
                _ => throw new ArgumentException($"Unknown TRADING_MODE: {tradingMode}. Valid values: live, live-real, backtest, optimize, walkforward, montecarlo, download")
            };

            AnsiConsole.Write(new FigletText("Trading Bot").Color(Color.Cyan1));
            AnsiConsole.MarkupLine("[grey]ADX Trend Following Strategy with Risk Management[/]");
            AnsiConsole.MarkupLine($"[green]Auto-starting mode:[/] {mode}\n");
        }
        else
        {
            // Interactive mode - show menu
            mode = menu.PromptMode();
        }

        await dispatcher.DispatchAsync(mode);
    }
}
