using ComplexBot.Configuration;

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

        var mode = menu.PromptMode();
        await dispatcher.DispatchAsync(mode);
    }
}
