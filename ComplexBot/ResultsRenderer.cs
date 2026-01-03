using Spectre.Console;
using System.Linq;
using ComplexBot.Models;
using ComplexBot.Services.Analytics;
using ComplexBot.Services.Backtesting;
using ComplexBot.Services.Strategies;

namespace ComplexBot;

class ResultsRenderer
{
    public void DisplayBacktestResults(BacktestResult result)
    {
        var m = result.Metrics;

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Backtest Results[/]").RuleStyle("grey"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn("Value");

        table.AddRow("Strategy", result.StrategyName);
        table.AddRow("Period", $"{result.StartDate:yyyy-MM-dd} → {result.EndDate:yyyy-MM-dd}");
        table.AddRow("Initial Capital", $"${result.InitialCapital:N2}");
        table.AddRow("Final Capital", FormatValue(result.FinalCapital, result.InitialCapital));
        table.AddRow("Total Return", FormatPercent(m.TotalReturn));
        table.AddRow("Annualized Return", FormatPercent(m.AnnualizedReturn));

        table.AddEmptyRow();

        table.AddRow("[yellow]Risk Metrics[/]", "");
        table.AddRow("Max Drawdown", $"[red]-{m.MaxDrawdownPercent:F2}%[/]");
        table.AddRow("Sharpe Ratio", FormatRatio(m.SharpeRatio));
        table.AddRow("Sortino Ratio", FormatRatio(m.SortinoRatio));
        table.AddRow("Profit Factor", FormatRatio(m.ProfitFactor));

        table.AddEmptyRow();

        table.AddRow("[yellow]Trade Statistics[/]", "");
        table.AddRow("Total Trades", m.TotalTrades.ToString());
        table.AddRow("Win Rate", FormatPercent(m.WinRate));
        table.AddRow("Winning/Losing", $"[green]{m.WinningTrades}[/] / [red]{m.LosingTrades}[/]");
        table.AddRow("Average Win", $"[green]+${m.AverageWin:N2}[/]");
        table.AddRow("Average Loss", $"[red]-${Math.Abs(m.AverageLoss):N2}[/]");
        table.AddRow("Largest Win", $"[green]+${m.LargestWin:N2}[/]");
        table.AddRow("Largest Loss", $"[red]${m.LargestLoss:N2}[/]");
        table.AddRow("Avg Holding", $"{m.AverageHoldingPeriod.TotalDays:F1} days");

        AnsiConsole.Write(table);

        var assessment = GetStrategyAssessment(m);
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(assessment.message)
            .Header($"[{assessment.color}]Assessment[/]")
            .Border(BoxBorder.Rounded));
    }

    public void DisplayWalkForwardResults(WalkForwardResult result)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Walk-Forward Analysis[/]").RuleStyle("grey"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn("Value");

        table.AddRow("Walk-Forward Efficiency", FormatWfe(result.WalkForwardEfficiency));
        table.AddRow("OOS Consistency", FormatPercent(result.OosConsistency));
        table.AddRow("Avg OOS Return", FormatPercent(result.AverageOosReturn));
        table.AddRow("Avg OOS Sharpe", FormatRatio(result.AverageOosSharpe));
        table.AddRow("Avg OOS Max DD", $"[red]-{result.AverageOosMaxDrawdown:F2}%[/]");
        table.AddRow("Periods Tested", result.Periods.Count.ToString());
        table.AddRow("Strategy Robust?", result.IsRobust ? "[green]YES[/]" : "[red]NO[/]");

        AnsiConsole.Write(table);

        if (result.Periods.Count > 0)
        {
            AnsiConsole.WriteLine();
            var periodTable = new Table()
                .Border(TableBorder.Simple)
                .AddColumn("Period")
                .AddColumn("IS Return")
                .AddColumn("OOS Return")
                .AddColumn("WFE");

            int i = 1;
            foreach (var period in result.Periods)
            {
                periodTable.AddRow(
                    $"#{i++}",
                    FormatPercent(period.InSampleResult.Metrics.TotalReturn),
                    FormatPercent(period.OutOfSampleResult.Metrics.TotalReturn),
                    FormatWfe(period.WfeForPeriod)
                );
            }

            AnsiConsole.Write(periodTable);
        }
    }

    public void DisplayMonteCarloResults(MonteCarloResult result, BacktestResult backtest)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Monte Carlo Analysis[/]").RuleStyle("grey"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn("Value");

        table.AddRow("Original Return", FormatPercent(result.OriginalReturn));
        table.AddRow("Median Return (simulated)", FormatPercent(result.MedianReturn));
        table.AddRow("5th Percentile Return", FormatPercent(result.Percentile5Return));
        table.AddRow("95th Percentile Return", FormatPercent(result.Percentile95Return));
        table.AddRow("Avg Max Drawdown", $"[red]-{result.AverageMaxDrawdown:F2}%[/]");
        table.AddRow("95th Percentile DD", $"[red]-{result.Percentile95Drawdown:F2}%[/]");
        table.AddRow("Ruin Probability", FormatRuinProb(result.RuinProbability));

        AnsiConsole.Write(table);

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(result.GetConfidenceAssessment())
            .Header("[cyan]Risk Assessment[/]")
            .Border(BoxBorder.Rounded));
    }

    public void DisplayOptimizationResults(OptimizationResult result)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Optimization Results[/]").RuleStyle("grey"));

        var summaryTable = new Table().Border(TableBorder.Rounded)
            .AddColumn("Metric").AddColumn("Value");

        summaryTable.AddRow("Total combinations", result.TotalCombinations.ToString());
        summaryTable.AddRow("Valid combinations", result.ValidCombinations.ToString());
        summaryTable.AddRow("In-Sample Period", $"{result.InSampleStart:yyyy-MM-dd} → {result.InSampleEnd:yyyy-MM-dd}");
        summaryTable.AddRow("Out-of-Sample Period", $"{result.OutOfSampleStart:yyyy-MM-dd} → {result.OutOfSampleEnd:yyyy-MM-dd}");
        summaryTable.AddRow("Robust Results", result.RobustResults.Count.ToString());

        AnsiConsole.Write(summaryTable);

        if (result.TopResults.Any())
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[cyan]Top 10 Parameter Sets[/]").RuleStyle("grey"));

            var resultsTable = new Table()
                .Border(TableBorder.Simple)
                .AddColumn("#")
                .AddColumn("ADX")
                .AddColumn("Threshold")
                .AddColumn("Fast/Slow EMA")
                .AddColumn("ATR×")
                .AddColumn("Vol×")
                .AddColumn("IS Return")
                .AddColumn("OOS Return")
                .AddColumn("IS Sharpe")
                .AddColumn("OOS Sharpe")
                .AddColumn("Robust?");

            int rank = 1;
            foreach (var r in result.TopResults.Take(10))
            {
                var p = r.Parameters;
                var isM = r.InSampleResult.Metrics;
                var oosM = r.OutOfSampleResult.Metrics;

                resultsTable.AddRow(
                    rank++.ToString(),
                    p.AdxPeriod.ToString(),
                    $"{p.AdxThreshold}",
                    $"{p.FastEmaPeriod}/{p.SlowEmaPeriod}",
                    $"{p.AtrStopMultiplier:F1}",
                    $"{p.VolumeThreshold:F1}",
                    FormatPercent(isM.TotalReturn),
                    FormatPercent(oosM.TotalReturn),
                    FormatRatio(isM.SharpeRatio),
                    FormatRatio(oosM.SharpeRatio),
                    r.IsRobust ? "[green]✓[/]" : "[red]✗[/]"
                );
            }

            AnsiConsole.Write(resultsTable);
        }

        if (result.BestRobustParameters != null)
        {
            var best = result.RobustResults.First();
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Panel(
                $"ADX Period: {best.Parameters.AdxPeriod}\n" +
                $"ADX Threshold: {best.Parameters.AdxThreshold}\n" +
                $"Fast EMA: {best.Parameters.FastEmaPeriod}\n" +
                $"Slow EMA: {best.Parameters.SlowEmaPeriod}\n" +
                $"ATR Multiplier: {best.Parameters.AtrStopMultiplier}\n" +
                $"Volume Threshold: {best.Parameters.VolumeThreshold}\n" +
                $"─────────────────────\n" +
                $"OOS Sharpe: {best.OutOfSampleResult.Metrics.SharpeRatio:F2}\n" +
                $"OOS Return: {best.OutOfSampleResult.Metrics.TotalReturn:F1}%\n" +
                $"OOS Max DD: {best.OutOfSampleResult.Metrics.MaxDrawdownPercent:F1}%\n" +
                $"Robustness: {best.Robustness:F0}%"
            ).Header("[green]Best Robust Parameters[/]").Border(BoxBorder.Double));
        }
        else
        {
            AnsiConsole.MarkupLine("\n[red]⚠ No robust parameter sets found. Try wider ranges or more data.[/]");
        }
    }

    public void DisplayEnsembleOptimizationResults(
        GeneticOptimizationResult<EnsembleOptimizationSettings> result,
        BacktestResult backtestResult)
    {
        var best = result.BestSettings;

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Ensemble Optimization Results[/]").RuleStyle("grey"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn("Value");

        table.AddRow("Best fitness", result.BestFitness.ToString("F2"));
        table.AddRow("Minimum agreement", $"{best.MinimumAgreement:P0}");
        table.AddRow("Use confidence weighting", best.UseConfidenceWeighting ? "Yes" : "No");
        table.AddRow("ADX weight", best.AdxWeight.ToString("F2"));
        table.AddRow("MA weight", best.MaWeight.ToString("F2"));
        table.AddRow("RSI weight", best.RsiWeight.ToString("F2"));

        AnsiConsole.Write(table);

        DisplayBacktestResults(backtestResult);
    }

    public void DisplayMaOptimizationResults(
        GeneticOptimizationResult<MaStrategySettings> result,
        BacktestResult backtestResult)
    {
        var best = result.BestSettings;

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]MA Optimization Results[/]").RuleStyle("grey"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn("Value");

        table.AddRow("Best fitness", result.BestFitness.ToString("F2"));
        table.AddRow("Fast/Slow MA", $"{best.FastMaPeriod} / {best.SlowMaPeriod}");
        table.AddRow("ATR stop", best.AtrStopMultiplier.ToString("F2"));
        table.AddRow("Take profit", best.TakeProfitMultiplier.ToString("F2"));
        table.AddRow("Volume threshold", best.VolumeThreshold.ToString("F2"));
        table.AddRow("Volume required", best.RequireVolumeConfirmation ? "Yes" : "No");

        AnsiConsole.Write(table);

        DisplayBacktestResults(backtestResult);
    }

    public void DisplayRsiOptimizationResults(
        GeneticOptimizationResult<RsiStrategySettings> result,
        BacktestResult backtestResult)
    {
        var best = result.BestSettings;

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]RSI Optimization Results[/]").RuleStyle("grey"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn("Value");

        table.AddRow("Best fitness", result.BestFitness.ToString("F2"));
        table.AddRow("RSI period", best.RsiPeriod.ToString());
        table.AddRow("Oversold/Overbought", $"{best.OversoldLevel:F1} / {best.OverboughtLevel:F1}");
        table.AddRow("ATR stop", best.AtrStopMultiplier.ToString("F2"));
        table.AddRow("Take profit", best.TakeProfitMultiplier.ToString("F2"));
        table.AddRow("Trend filter", best.UseTrendFilter ? $"Yes ({best.TrendFilterPeriod})" : "No");
        table.AddRow("Exit on neutral", best.ExitOnNeutral ? "Yes" : "No");
        table.AddRow("Volume threshold", best.VolumeThreshold.ToString("F2"));
        table.AddRow("Volume required", best.RequireVolumeConfirmation ? "Yes" : "No");

        AnsiConsole.Write(table);

        DisplayBacktestResults(backtestResult);
    }

    public void DisplayFullEnsembleResults(
        GeneticOptimizationResult<FullEnsembleSettings> result,
        BacktestResult backtestResult)
    {
        var best = result.BestSettings;

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Full Ensemble Optimization Results[/]").RuleStyle("grey"));

        var weightsTable = new Table()
            .Border(TableBorder.Rounded)
            .Title("[cyan]Strategy Weights[/]")
            .AddColumn("Strategy")
            .AddColumn("Weight");

        weightsTable.AddRow("ADX Trend Following", $"{best.AdxWeight:P0}");
        weightsTable.AddRow("MA Crossover", $"{best.MaWeight:P0}");
        weightsTable.AddRow("RSI Mean Reversion", $"{best.RsiWeight:P0}");
        weightsTable.AddRow("Minimum Agreement", $"{best.MinimumAgreement:P0}");
        weightsTable.AddRow("Confidence Weighting", best.UseConfidenceWeighting ? "Yes" : "No");

        AnsiConsole.Write(weightsTable);

        var adxTable = new Table()
            .Border(TableBorder.Simple)
            .Title("[cyan]ADX Strategy Parameters[/]")
            .AddColumn("Parameter")
            .AddColumn("Value");

        adxTable.AddRow("ADX Period", best.AdxPeriod.ToString());
        adxTable.AddRow("ADX Threshold", $"{best.AdxThreshold:F1}");
        adxTable.AddRow("ADX Exit", $"{best.AdxExitThreshold:F1}");
        adxTable.AddRow("Fast/Slow EMA", $"{best.AdxFastEmaPeriod}/{best.AdxSlowEmaPeriod}");
        adxTable.AddRow("ATR Stop", $"{best.AdxAtrStopMultiplier:F2}x");
        adxTable.AddRow("Volume", $"{best.AdxVolumeThreshold:F2}x");

        AnsiConsole.Write(adxTable);

        var maTable = new Table()
            .Border(TableBorder.Simple)
            .Title("[cyan]MA Strategy Parameters[/]")
            .AddColumn("Parameter")
            .AddColumn("Value");

        maTable.AddRow("Fast/Slow MA", $"{best.MaFastPeriod}/{best.MaSlowPeriod}");
        maTable.AddRow("ATR Stop", $"{best.MaAtrStopMultiplier:F2}x");
        maTable.AddRow("Take Profit", $"{best.MaTakeProfitMultiplier:F2}x");
        maTable.AddRow("Volume", $"{best.MaVolumeThreshold:F2}x");

        AnsiConsole.Write(maTable);

        var rsiTable = new Table()
            .Border(TableBorder.Simple)
            .Title("[cyan]RSI Strategy Parameters[/]")
            .AddColumn("Parameter")
            .AddColumn("Value");

        rsiTable.AddRow("RSI Period", best.RsiPeriod.ToString());
        rsiTable.AddRow("Oversold/Overbought", $"{best.RsiOversoldLevel:F1}/{best.RsiOverboughtLevel:F1}");
        rsiTable.AddRow("ATR Stop", $"{best.RsiAtrStopMultiplier:F2}x");
        rsiTable.AddRow("Take Profit", $"{best.RsiTakeProfitMultiplier:F2}x");
        rsiTable.AddRow("Trend Filter", best.RsiUseTrendFilter ? "Yes" : "No");

        AnsiConsole.Write(rsiTable);

        AnsiConsole.MarkupLine($"\n[green]Best Fitness: {result.BestFitness:F2}[/]");

        DisplayBacktestResults(backtestResult);
    }

    private static string FormatPercent(decimal value) =>
        value >= 0 ? $"[green]+{value:F2}%[/]" : $"[red]{value:F2}%[/]";

    private static string FormatValue(decimal value, decimal reference) =>
        value >= reference ? $"[green]${value:N2}[/]" : $"[red]${value:N2}[/]";

    private static string FormatRatio(decimal value) =>
        value switch
        {
            >= 2 => $"[green]{value:F2}[/]",
            >= 1 => $"[yellow]{value:F2}[/]",
            _ => $"[red]{value:F2}[/]"
        };

    private static string FormatWfe(decimal value) =>
        value switch
        {
            >= 70 => $"[green]{value:F1}%[/]",
            >= 50 => $"[yellow]{value:F1}%[/]",
            _ => $"[red]{value:F1}%[/]"
        };

    private static string FormatRuinProb(decimal value) =>
        value switch
        {
            < 1 => $"[green]{value:F2}%[/]",
            < 5 => $"[yellow]{value:F2}%[/]",
            _ => $"[red]{value:F2}%[/]"
        };

    private static (string color, string message) GetStrategyAssessment(PerformanceMetrics m)
    {
        var issues = new List<string>();
        var strengths = new List<string>();

        if (m.SharpeRatio >= 1.5m) strengths.Add("Excellent risk-adjusted returns");
        else if (m.SharpeRatio >= 1.0m) strengths.Add("Good risk-adjusted returns");
        else if (m.SharpeRatio < 0.5m) issues.Add("Low Sharpe ratio - consider adjustments");

        if (m.MaxDrawdownPercent <= 15m) strengths.Add("Well-controlled drawdowns");
        else if (m.MaxDrawdownPercent > 25m) issues.Add("High max drawdown - reduce position sizes");

        if (m.ProfitFactor >= 1.5m) strengths.Add("Strong profit factor");
        else if (m.ProfitFactor < 1.2m) issues.Add("Low profit factor");

        if (m.WinRate >= 50) strengths.Add($"Solid win rate ({m.WinRate:F0}%)");
        else if (m.WinRate < 40) issues.Add("Low win rate - ensure avg win > avg loss");

        if (m.TotalTrades < 30) issues.Add("Insufficient trades for statistical significance");

        string color = issues.Count == 0 ? "green" : issues.Count <= 2 ? "yellow" : "red";

        var message = string.Join("\n",
            strengths.Select(s => $"✓ {s}")
            .Concat(issues.Select(i => $"⚠ {i}"))
        );

        return (color, message);
    }
}
