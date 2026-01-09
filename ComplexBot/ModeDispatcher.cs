using ComplexBot.Configuration;
using TradingBot.Core.Models;
using ComplexBot.Models;

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

    public async Task DispatchAsync(AppMode mode)
    {
        switch (mode)
        {
            case AppMode.Backtest:
                await _backtestRunner.RunBacktest();
                break;
            case AppMode.ParameterOptimization:
                await _optimizationRunner.RunOptimization();
                break;
            case AppMode.WalkForwardAnalysis:
                await _analysisRunner.RunWalkForward();
                break;
            case AppMode.MonteCarloSimulation:
                await _analysisRunner.RunMonteCarlo();
                break;
            case AppMode.LiveTradingPaper:
                await _liveTradingRunner.RunLiveTrading(paperTrade: true);
                break;
            case AppMode.LiveTradingReal:
                await _liveTradingRunner.RunLiveTrading(paperTrade: false);
                break;
            case AppMode.DownloadData:
                await _dataRunner.DownloadData();
                break;
            case AppMode.ConfigurationSettings:
                _settingsService.ConfigureSettings();
                break;
            case AppMode.ResetToDefaults:
                _configService.ResetToDefaults();
                break;
            case AppMode.Exit:
                break;
        }
    }
}
