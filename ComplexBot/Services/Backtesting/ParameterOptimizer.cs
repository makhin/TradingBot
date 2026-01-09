using TradingBot.Core.Models;
using ComplexBot.Services.Strategies;
using TradingBot.Core.RiskManagement;

namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Grid Search Parameter Optimizer
/// Перебирает комбинации параметров и находит лучшие по выбранной метрике
/// С защитой от переобучения через out-of-sample тестирование
/// </summary>
public class ParameterOptimizer
{
    private readonly OptimizerSettings _settings;
    private readonly PerformanceFitnessCalculator _fitnessCalculator;

    public ParameterOptimizer(OptimizerSettings? settings = null)
    {
        _settings = settings ?? new OptimizerSettings();
        _fitnessCalculator = new PerformanceFitnessCalculator(_settings.Policy);
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

            // Skip if not enough trades or exceeds policy limits
            if (!_fitnessCalculator.MeetsPolicy(isResult.Metrics))
                continue;

            // Test on out-of-sample data
            var oosStrategy = new AdxTrendStrategy(parameters);
            var oosEngine = new BacktestEngine(oosStrategy, riskSettings, backtestSettings);
            var oosResult = oosEngine.Run(outOfSampleData, symbol);

            var inSampleScore = CalculateScore(isResult.Metrics);
            var outOfSampleScore = CalculateScore(oosResult.Metrics);

            results.Add(new ParameterSetResult(
                parameters,
                isResult,
                oosResult,
                inSampleScore,
                outOfSampleScore
            ));
        }

        // Sort by selected metric
        var sorted = results.OrderByDescending(r => r.InSampleScore);
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
        return _fitnessCalculator.CalculateScore(_settings.OptimizeFor, metrics);
    }
}
