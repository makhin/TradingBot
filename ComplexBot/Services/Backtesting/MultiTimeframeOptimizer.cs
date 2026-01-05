using Binance.Net.Enums;
using ComplexBot.Models;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.Trading;
using ComplexBot.Services.Trading.SignalFilters;

namespace ComplexBot.Services.Backtesting;

public class MultiTimeframeOptimizer
{
    private readonly HistoricalDataLoader _loader;
    private readonly MultiTimeframeBacktester _backtester;

    public MultiTimeframeOptimizer(
        HistoricalDataLoader? loader = null,
        MultiTimeframeBacktester? backtester = null)
    {
        _loader = loader ?? new HistoricalDataLoader();
        _backtester = backtester ?? new MultiTimeframeBacktester();
    }

    public async Task<List<MultiTimeframeOptimizationResult>> OptimizeAsync(
        string symbol,
        List<Candle> primaryCandles,
        Func<IStrategy> primaryStrategyFactory,
        Func<decimal, IStrategy> adxFilterStrategyFactory,
        Func<decimal, decimal, IStrategy> rsiFilterStrategyFactory,
        MultiTimeframeOptimizationSettings settings,
        RiskSettings riskSettings,
        BacktestSettings backtestSettings,
        CancellationToken cancellationToken = default)
    {
        var results = new List<MultiTimeframeOptimizationResult>();

        if (primaryCandles.Count == 0)
            return results;

        if (settings.TestNoFilterBaseline)
        {
            var baseline = _backtester.Run(
                symbol,
                primaryCandles,
                primaryStrategyFactory(),
                Array.Empty<MultiTimeframeFilterDefinition>(),
                riskSettings,
                backtestSettings);

            results.Add(CreateResult(
                "No Filter (Baseline)",
                null,
                null,
                null,
                new Dictionary<string, decimal>(),
                baseline,
                settings.OptimizeFor,
                isBaseline: true));
        }

        if (!settings.OptimizeFilters)
            return results;

        var filterIntervals = settings.FilterIntervalCandidates ?? Array.Empty<string>();
        if (filterIntervals.Length == 0)
            return results;

        var start = primaryCandles.First().OpenTime;
        var end = primaryCandles.Last().CloseTime;

        var filterCandlesByInterval = await LoadFilterCandlesAsync(
            symbol,
            filterIntervals,
            start,
            end,
            cancellationToken);

        foreach (var interval in filterIntervals)
        {
            if (!filterCandlesByInterval.TryGetValue(interval, out var intervalCandles))
                continue;

            if (intervalCandles.Count == 0)
                continue;

            foreach (var overbought in settings.RsiOverboughtRange)
            {
                foreach (var oversold in settings.RsiOversoldRange)
                {
                    if (oversold >= overbought)
                        continue;

                    foreach (var mode in settings.FilterModesToTest)
                    {
                        var filter = new RsiSignalFilter(
                            overboughtThreshold: overbought,
                            oversoldThreshold: oversold,
                            mode: mode);

                        var strategy = rsiFilterStrategyFactory(overbought, oversold);
                        var filterDef = new MultiTimeframeFilterDefinition(
                            "RSI",
                            strategy,
                            filter,
                            intervalCandles);

                        var backtest = _backtester.Run(
                            symbol,
                            primaryCandles,
                            primaryStrategyFactory(),
                            new[] { filterDef },
                            riskSettings,
                            backtestSettings);

                        var parameters = new Dictionary<string, decimal>
                        {
                            ["Overbought"] = overbought,
                            ["Oversold"] = oversold
                        };

                        results.Add(CreateResult(
                            $"RSI {overbought}/{oversold} {interval} {mode}",
                            interval,
                            "RSI",
                            mode,
                            parameters,
                            backtest,
                            settings.OptimizeFor,
                            isBaseline: false));
                    }
                }
            }

            foreach (var minTrend in settings.AdxMinThresholdRange)
            {
                var strongRange = settings.AdxStrongThresholdRange?.Length > 0
                    ? settings.AdxStrongThresholdRange
                    : [minTrend + 10m];

                foreach (var strongTrend in strongRange)
                {
                    if (strongTrend < minTrend)
                        continue;

                    foreach (var mode in settings.FilterModesToTest)
                    {
                        var filter = new AdxSignalFilter(
                            minTrendStrength: minTrend,
                            strongTrendThreshold: strongTrend,
                            mode: mode);

                        var strategy = adxFilterStrategyFactory(minTrend);
                        var filterDef = new MultiTimeframeFilterDefinition(
                            "ADX",
                            strategy,
                            filter,
                            intervalCandles);

                        var backtest = _backtester.Run(
                            symbol,
                            primaryCandles,
                            primaryStrategyFactory(),
                            new[] { filterDef },
                            riskSettings,
                            backtestSettings);

                        var parameters = new Dictionary<string, decimal>
                        {
                            ["MinAdx"] = minTrend,
                            ["StrongAdx"] = strongTrend
                        };

                        results.Add(CreateResult(
                            $"ADX {minTrend}/{strongTrend} {interval} {mode}",
                            interval,
                            "ADX",
                            mode,
                            parameters,
                            backtest,
                            settings.OptimizeFor,
                            isBaseline: false));
                    }
                }
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .ToList();
    }

    private async Task<Dictionary<string, List<Candle>>> LoadFilterCandlesAsync(
        string symbol,
        string[] intervals,
        DateTime start,
        DateTime end,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, List<Candle>>(StringComparer.OrdinalIgnoreCase);

        foreach (var interval in intervals.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(interval))
                continue;

            KlineInterval parsed = KlineIntervalExtensions.Parse(interval);
            var candles = await _loader.LoadAsync(symbol, parsed, start, end);
            results[interval] = candles;
        }

        return results;
    }

    private static MultiTimeframeOptimizationResult CreateResult(
        string configuration,
        string? interval,
        string? strategy,
        FilterMode? mode,
        Dictionary<string, decimal> parameters,
        MultiTimeframeBacktestResult backtest,
        OptimizationTarget optimizeFor,
        bool isBaseline)
    {
        var score = CalculateScore(backtest.Result.Metrics, optimizeFor);

        return new MultiTimeframeOptimizationResult(
            configuration,
            interval,
            strategy,
            mode,
            parameters,
            backtest,
            score,
            isBaseline);
    }

    private static decimal CalculateScore(PerformanceMetrics metrics, OptimizationTarget target)
    {
        if (metrics.TotalTrades == 0)
            return -999;

        return target switch
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
