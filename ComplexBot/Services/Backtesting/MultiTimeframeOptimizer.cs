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
        IProgress<(int current, int total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<MultiTimeframeOptimizationResult>();

        if (primaryCandles.Count == 0)
            return results;

        var filterIntervals = (settings.FilterIntervalCandidates ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var start = primaryCandles.First().OpenTime;
        var end = primaryCandles.Last().CloseTime;
        var filterCandlesByInterval = new Dictionary<string, List<Candle>>(StringComparer.OrdinalIgnoreCase);
        var intervalsWithData = new List<string>();

        if (settings.OptimizeFilters && filterIntervals.Length > 0)
        {
            filterCandlesByInterval = await LoadFilterCandlesAsync(
                symbol,
                filterIntervals,
                start,
                end,
                cancellationToken);

            intervalsWithData = filterIntervals
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(interval =>
                    filterCandlesByInterval.TryGetValue(interval, out var candles) && candles.Count > 0)
                .ToList();
        }

        var totalSteps = 0;
        if (settings.TestNoFilterBaseline)
        {
            totalSteps++;
        }

        if (settings.OptimizeFilters && intervalsWithData.Count > 0)
        {
            totalSteps += CountFilterCombinations(intervalsWithData, settings);
        }

        var completedSteps = 0;
        progress?.Report((completedSteps, totalSteps));

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

            completedSteps++;
            progress?.Report((completedSteps, totalSteps));
        }

        if (!settings.OptimizeFilters || intervalsWithData.Count == 0)
            return results;

        foreach (var interval in intervalsWithData)
        {
            var intervalCandles = filterCandlesByInterval[interval];

            var rsiOverboughtRange = (settings.RsiOverboughtRange ?? Array.Empty<decimal>()).Distinct().ToArray();
            var rsiOversoldRange = (settings.RsiOversoldRange ?? Array.Empty<decimal>()).Distinct().ToArray();
            var filterModes = (settings.FilterModesToTest ?? Array.Empty<FilterMode>()).Distinct().ToArray();
            var adxMinRange = (settings.AdxMinThresholdRange ?? Array.Empty<decimal>()).Distinct().ToArray();
            var adxStrongRange = (settings.AdxStrongThresholdRange ?? Array.Empty<decimal>()).Distinct().ToArray();

            foreach (var overbought in rsiOverboughtRange)
            {
                foreach (var oversold in rsiOversoldRange)
                {
                    if (oversold >= overbought)
                        continue;

                    foreach (var mode in filterModes)
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

                        completedSteps++;
                        progress?.Report((completedSteps, totalSteps));
                    }
                }
            }

            foreach (var minTrend in adxMinRange)
            {
                var strongRange = adxStrongRange.Length > 0
                    ? adxStrongRange
                    : [minTrend + 10m];

                foreach (var strongTrend in strongRange)
                {
                    if (strongTrend < minTrend)
                        continue;

                    foreach (var mode in filterModes)
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

                        completedSteps++;
                        progress?.Report((completedSteps, totalSteps));
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
            var candles = await _loader.LoadFromDiskOrDownloadAsync(symbol, parsed, start, end);
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

    private static int CountFilterCombinations(
        IReadOnlyCollection<string> intervalsWithData,
        MultiTimeframeOptimizationSettings settings)
    {
        if (intervalsWithData.Count == 0)
            return 0;

        var modeCount = settings.FilterModesToTest?.Distinct().Count() ?? 0;
        if (modeCount == 0)
            return 0;

        var rsiCombos = CountRsiCombinations(settings) * modeCount;
        var adxCombos = CountAdxCombinations(settings, modeCount);
        return intervalsWithData.Count * (rsiCombos + adxCombos);
    }

    private static int CountRsiCombinations(MultiTimeframeOptimizationSettings settings)
    {
        var overboughtRange = settings.RsiOverboughtRange?.Distinct().ToArray() ?? Array.Empty<decimal>();
        var oversoldRange = settings.RsiOversoldRange?.Distinct().ToArray() ?? Array.Empty<decimal>();
        if (overboughtRange.Length == 0 || oversoldRange.Length == 0)
            return 0;

        var combos = 0;
        foreach (var overbought in overboughtRange)
        {
            foreach (var oversold in oversoldRange)
            {
                if (oversold < overbought)
                {
                    combos++;
                }
            }
        }

        return combos;
    }

    private static int CountAdxCombinations(
        MultiTimeframeOptimizationSettings settings,
        int modeCount)
    {
        var minRange = settings.AdxMinThresholdRange?.Distinct().ToArray() ?? Array.Empty<decimal>();
        if (minRange.Length == 0)
            return 0;

        var strongRange = settings.AdxStrongThresholdRange?.Distinct().ToArray() ?? Array.Empty<decimal>();
        var combos = 0;

        foreach (var minTrend in minRange)
        {
            if (strongRange.Length == 0)
            {
                combos += modeCount;
                continue;
            }

            foreach (var strongTrend in strongRange)
            {
                if (strongTrend >= minTrend)
                {
                    combos += modeCount;
                }
            }
        }

        return combos;
    }
}
