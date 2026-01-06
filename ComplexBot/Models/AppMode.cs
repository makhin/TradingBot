namespace ComplexBot.Models;

public enum AppMode
{
    Backtest,
    ParameterOptimization,
    WalkForwardAnalysis,
    MonteCarloSimulation,
    LiveTradingPaper,
    LiveTradingReal,
    DownloadData,
    ConfigurationSettings,
    ResetToDefaults,
    Exit
}
