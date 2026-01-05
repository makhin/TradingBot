namespace ComplexBot.Configuration;

public class BotConfiguration
{
    public BinanceApiSettings BinanceApi { get; set; } = new();
    public TelegramSettings Telegram { get; set; } = new();
    public RiskManagementSettings RiskManagement { get; set; } = new();
    public PortfolioRiskConfigSettings PortfolioRisk { get; set; } = new();
    public StrategyConfigSettings Strategy { get; set; } = new();
    public EnsembleConfigSettings Ensemble { get; set; } = new();
    public EnsembleOptimizerConfigSettings EnsembleOptimizer { get; set; } = new();
    public MaStrategyConfigSettings MaStrategy { get; set; } = new();
    public RsiStrategyConfigSettings RsiStrategy { get; set; } = new();
    public MaOptimizerConfigSettings MaOptimizer { get; set; } = new();
    public RsiOptimizerConfigSettings RsiOptimizer { get; set; } = new();
    public BacktestingSettings Backtesting { get; set; } = new();
    public LiveTradingSettings LiveTrading { get; set; } = new();
    public OptimizationSettings Optimization { get; set; } = new();
    public GeneticOptimizerConfigSettings GeneticOptimizer { get; set; } = new();
}
