// Global imports for backward compatibility
global using BinanceApiSettings = ComplexBot.Configuration.External.BinanceApiSettings;
global using TelegramSettings = ComplexBot.Configuration.External.TelegramSettings;
global using RiskManagementSettings = ComplexBot.Configuration.Settings.RiskManagementSettings;
global using PortfolioRiskConfigSettings = ComplexBot.Configuration.Settings.PortfolioRiskConfigSettings;
global using StrategyConfigSettings = ComplexBot.Configuration.Strategy.StrategyConfigSettings;
global using EnsembleConfigSettings = ComplexBot.Configuration.Strategy.EnsembleConfigSettings;
global using MaStrategyConfigSettings = ComplexBot.Configuration.Strategy.MaStrategyConfigSettings;
global using RsiStrategyConfigSettings = ComplexBot.Configuration.Strategy.RsiStrategyConfigSettings;
global using EnsembleOptimizerConfigSettings = ComplexBot.Configuration.Optimization.EnsembleOptimizerConfigSettings;
global using MaOptimizerConfigSettings = ComplexBot.Configuration.Optimization.MaOptimizerConfigSettings;
global using RsiOptimizerConfigSettings = ComplexBot.Configuration.Optimization.RsiOptimizerConfigSettings;
global using GeneticOptimizerConfigSettings = ComplexBot.Configuration.Optimization.GeneticOptimizerConfigSettings;
global using BacktestingSettings = ComplexBot.Configuration.Optimization.BacktestingSettings;
global using OptimizationSettings = ComplexBot.Configuration.Optimization.OptimizationSettings;
global using MultiTimeframeOptimizerSettings = ComplexBot.Configuration.Optimization.MultiTimeframeOptimizerSettings;
global using LiveTradingSettings = ComplexBot.Configuration.Trading.LiveTradingSettings;
global using MultiPairLiveTradingSettings = ComplexBot.Configuration.Trading.MultiPairLiveTradingSettings;
global using TradingPairConfig = ComplexBot.Configuration.Trading.TradingPairConfig;

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
    public MultiPairLiveTradingSettings MultiPairLiveTrading { get; set; } = new();
    public OptimizationSettings Optimization { get; set; } = new();
    public MultiTimeframeOptimizerSettings MultiTimeframeOptimizer { get; set; } = new();
    public GeneticOptimizerConfigSettings GeneticOptimizer { get; set; } = new();
}
