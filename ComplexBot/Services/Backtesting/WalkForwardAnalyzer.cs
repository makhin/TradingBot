using TradingBot.Core.Models;
using ComplexBot.Services.Strategies;
using TradingBot.Core.RiskManagement;

namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Walk-Forward Analyzer - tests strategy robustness by simulating real trading conditions
/// Splits data into in-sample (optimization) and out-of-sample (validation) periods
/// </summary>
public class WalkForwardAnalyzer
{
    private readonly WalkForwardSettings _settings;

    public WalkForwardAnalyzer(WalkForwardSettings? settings = null)
    {
        _settings = settings ?? new WalkForwardSettings();
    }

    public WalkForwardResult Analyze(
        List<Candle> candles,
        string symbol,
        Func<IStrategy> strategyFactory,
        RiskSettings riskSettings,
        BacktestSettings backtestSettings)
    {
        var periods = new List<WalkForwardPeriod>();
        int totalBars = candles.Count;
        int windowSize = (int)(totalBars * _settings.InSampleRatio);
        int oosSize = (int)(totalBars * _settings.OutOfSampleRatio);
        int stepSize = (int)(totalBars * _settings.StepRatio);

        int startIndex = 0;

        while (startIndex + windowSize + oosSize <= totalBars)
        {
            // In-sample period
            var isCandles = candles.Skip(startIndex).Take(windowSize).ToList();
            var isStrategy = strategyFactory();
            var isEngine = new BacktestEngine(isStrategy, riskSettings, backtestSettings);
            var isResult = isEngine.Run(isCandles, symbol);

            // Out-of-sample period
            var oosCandles = candles.Skip(startIndex + windowSize).Take(oosSize).ToList();
            var oosStrategy = strategyFactory();
            var oosEngine = new BacktestEngine(oosStrategy, riskSettings, backtestSettings);
            var oosResult = oosEngine.Run(oosCandles, symbol);

            periods.Add(new WalkForwardPeriod(
                isCandles.First().OpenTime,
                isCandles.Last().CloseTime,
                oosCandles.First().OpenTime,
                oosCandles.Last().CloseTime,
                isResult,
                oosResult
            ));

            startIndex += stepSize;
        }

        return CalculateResults(periods);
    }

    private WalkForwardResult CalculateResults(List<WalkForwardPeriod> periods)
    {
        if (periods.Count == 0)
        {
            return new WalkForwardResult(
                0, 0, 0, 0, 0, new List<WalkForwardPeriod>(), false
            );
        }

        // Calculate Walk-Forward Efficiency (WFE)
        decimal totalIsReturn = periods.Sum(p => p.InSampleResult.Metrics.AnnualizedReturn);
        decimal totalOosReturn = periods.Sum(p => p.OutOfSampleResult.Metrics.AnnualizedReturn);
        decimal avgIsReturn = totalIsReturn / periods.Count;
        decimal avgOosReturn = totalOosReturn / periods.Count;
        
        decimal wfe = avgIsReturn != 0 ? avgOosReturn / avgIsReturn * 100 : 0;

        // OOS consistency
        int profitablePeriods = periods.Count(p => p.OutOfSampleResult.Metrics.TotalReturn > 0);
        decimal oosConsistency = (decimal)profitablePeriods / periods.Count * 100;

        // Average metrics
        decimal avgOosSharpe = periods.Average(p => p.OutOfSampleResult.Metrics.SharpeRatio);
        decimal avgOosMaxDD = periods.Average(p => p.OutOfSampleResult.Metrics.MaxDrawdownPercent);

        bool isRobust = wfe >= _settings.MinWfeThreshold 
            && oosConsistency >= _settings.MinConsistencyThreshold
            && avgOosSharpe >= _settings.MinSharpeThreshold;

        return new WalkForwardResult(
            wfe,
            avgOosReturn,
            avgOosSharpe,
            avgOosMaxDD,
            oosConsistency,
            periods,
            isRobust
        );
    }
}
