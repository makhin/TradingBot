using ComplexBot.Configuration;

namespace ComplexBot;

class ModeDispatcher
{
    private readonly BacktestRunner _backtestRunner;
    private readonly OptimizationRunner _optimizationRunner;
    private readonly AnalysisRunner _analysisRunner;
    private readonly LiveTradingRunner _liveTradingRunner;
    private readonly DataRunner _dataRunner;
    private readonly SettingsService _settingsService;
    private readonly ConfigurationService _configService;

    public ModeDispatcher(
        BacktestRunner backtestRunner,
        OptimizationRunner optimizationRunner,
        AnalysisRunner analysisRunner,
        LiveTradingRunner liveTradingRunner,
        DataRunner dataRunner,
        SettingsService settingsService,
        ConfigurationService configService)
    {
        _backtestRunner = backtestRunner;
        _optimizationRunner = optimizationRunner;
        _analysisRunner = analysisRunner;
        _liveTradingRunner = liveTradingRunner;
        _dataRunner = dataRunner;
        _settingsService = settingsService;
        _configService = configService;
    }

    public async Task DispatchAsync(string mode)
    {
        switch (mode)
        {
            case "Backtest":
                await _backtestRunner.RunBacktest();
                break;
            case "Parameter Optimization":
                await _optimizationRunner.RunOptimization();
                break;
            case "Walk-Forward Analysis":
                await _analysisRunner.RunWalkForward();
                break;
            case "Monte Carlo Simulation":
                await _analysisRunner.RunMonteCarlo();
                break;
            case "Live Trading (Paper)":
                await _liveTradingRunner.RunLiveTrading(paperTrade: true);
                break;
            case "Live Trading (Real)":
                await _liveTradingRunner.RunLiveTrading(paperTrade: false);
                break;
            case "Download Data":
                await _dataRunner.DownloadData();
                break;
            case "Configuration Settings":
                _settingsService.ConfigureSettings();
                break;
            case "Reset to Defaults":
                _configService.ResetToDefaults();
                break;
        }
    }
}
