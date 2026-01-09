namespace ComplexBot.Configuration;

public class BotConfiguration
{
    public AppSettings App { get; set; } = new();
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
    public AdxOptimizerConfigSettings AdxOptimizer { get; set; } = new();
    public BacktestSettings Backtest { get; set; } = new();
    public LiveTradingSettings LiveTrading { get; set; } = new();
    public OptimizationSettings Optimization { get; set; } = new();
    public GeneticOptimizerConfigSettings GeneticOptimizer { get; set; } = new();
}
