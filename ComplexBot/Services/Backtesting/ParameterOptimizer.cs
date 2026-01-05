using ComplexBot.Models;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.RiskManagement;

namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Grid Search Parameter Optimizer
/// Перебирает комбинации параметров и находит лучшие по выбранной метрике
/// С защитой от переобучения через out-of-sample тестирование
/// </summary>
public class ParameterOptimizer
{
    private readonly OptimizerSettings _settings;

    public ParameterOptimizer(OptimizerSettings? settings = null)
    {
        _settings = settings ?? new OptimizerSettings();
    }

    public OptimizationResult Optimize(
        List<Candle> candles,
        string symbol,
        RiskSettings riskSettings,
        BacktestSettings backtestSettings,
        IProgress<OptimizationProgress>? progress = null)
    {
        // Split data: 70% in-sample, 30% out-of-sample
        int splitIndex = (int)(candles.Count * _settings.InSampleRatio);
        var inSampleData = candles.Take(splitIndex).ToList();
        var outOfSampleData = candles.Skip(splitIndex).ToList();

        var parameterSets = GenerateParameterSets();
        var results = new List<ParameterSetResult>();
        
        int total = parameterSets.Count;
        int current = 0;

        foreach (var parameters in parameterSets)
        {
            current++;
            progress?.Report(new OptimizationProgress(current, total, parameters));

            // Test on in-sample data
            var strategy = new AdxTrendStrategy(parameters);
            var engine = new BacktestEngine(strategy, riskSettings, backtestSettings);
            var isResult = engine.Run(inSampleData, symbol);

            // Skip if not enough trades
            if (isResult.Metrics.TotalTrades < _settings.MinTrades)
                continue;

            // Test on out-of-sample data
            var oosStrategy = new AdxTrendStrategy(parameters);
            var oosEngine = new BacktestEngine(oosStrategy, riskSettings, backtestSettings);
            var oosResult = oosEngine.Run(outOfSampleData, symbol);

            results.Add(new ParameterSetResult(
                parameters,
                isResult,
                oosResult,
                CalculateScore(isResult.Metrics),
                CalculateScore(oosResult.Metrics)
            ));
        }

        // Sort by selected metric
        var sorted = _settings.OptimizeFor switch
        {
            OptimizationTarget.SharpeRatio => results.OrderByDescending(r => r.InSampleScore),
            OptimizationTarget.SortinoRatio => results.OrderByDescending(r => r.InSampleResult.Metrics.SortinoRatio),
            OptimizationTarget.ProfitFactor => results.OrderByDescending(r => r.InSampleResult.Metrics.ProfitFactor),
            OptimizationTarget.TotalReturn => results.OrderByDescending(r => r.InSampleResult.Metrics.TotalReturn),
            OptimizationTarget.RiskAdjusted => results.OrderByDescending(r => 
                r.InSampleResult.Metrics.AnnualizedReturn / (r.InSampleResult.Metrics.MaxDrawdownPercent + 1)),
            _ => results.OrderByDescending(r => r.InSampleScore)
        };

        var topResults = sorted.Take(_settings.TopResultsCount).ToList();

        // Find robust parameters (good OOS performance)
        var robustResults = topResults
            .Where(r => r.OutOfSampleScore >= r.InSampleScore * _settings.MinRobustnessRatio)
            .OrderByDescending(r => r.OutOfSampleScore)
            .ToList();

        return new OptimizationResult(
            topResults,
            robustResults,
            parameterSets.Count,
            results.Count,
            inSampleData.First().OpenTime,
            inSampleData.Last().CloseTime,
            outOfSampleData.First().OpenTime,
            outOfSampleData.Last().CloseTime
        );
    }

    private List<StrategySettings> GenerateParameterSets()
    {
        var sets = new List<StrategySettings>();

        foreach (var adxPeriod in _settings.AdxPeriodRange)
        foreach (var adxThreshold in _settings.AdxThresholdRange)
        foreach (var fastEma in _settings.FastEmaRange)
        foreach (var slowEma in _settings.SlowEmaRange)
        foreach (var atrMultiplier in _settings.AtrMultiplierRange)
        foreach (var volumeThreshold in _settings.VolumeThresholdRange)
        {
            // Skip invalid combinations
            if (fastEma >= slowEma) continue;

            sets.Add(new StrategySettings
            {
                AdxPeriod = adxPeriod,
                AdxThreshold = adxThreshold,
                AdxExitThreshold = adxThreshold - 7, // Dynamic exit threshold
                FastEmaPeriod = fastEma,
                SlowEmaPeriod = slowEma,
                AtrPeriod = 14,
                AtrStopMultiplier = atrMultiplier,
                TakeProfitMultiplier = 1.5m,
                VolumeThreshold = volumeThreshold,
                RequireVolumeConfirmation = volumeThreshold > 1.0m,
                RequireObvConfirmation = true
            });
        }

        return sets;
    }

    private decimal CalculateScore(PerformanceMetrics metrics)
    {
        // Combined score: Sharpe weighted by other factors
        if (metrics.TotalTrades < _settings.MinTrades) return -999;
        if (metrics.MaxDrawdownPercent > 30) return -999;

        return _settings.OptimizeFor switch
        {
            OptimizationTarget.SharpeRatio => metrics.SharpeRatio,
            OptimizationTarget.SortinoRatio => metrics.SortinoRatio,
            OptimizationTarget.ProfitFactor => metrics.ProfitFactor,
            OptimizationTarget.TotalReturn => metrics.TotalReturn,
            OptimizationTarget.RiskAdjusted => 
                metrics.AnnualizedReturn / (metrics.MaxDrawdownPercent + 1) * (metrics.SharpeRatio + 1),
            _ => metrics.SharpeRatio
        };
    }
}
