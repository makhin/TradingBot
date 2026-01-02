using Binance.Net.Enums;
using Spectre.Console;
using ComplexBot.Models;
using ComplexBot.Services.Analytics;
using ComplexBot.Services.Backtesting;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.Trading;
using ComplexBot.Services.Notifications;
using ComplexBot.Configuration;

namespace ComplexBot;

class Program
{
    static ConfigurationService _configService = null!;

    static async Task Main(string[] args)
    {
        _configService = new ConfigurationService();

        AnsiConsole.Write(new FigletText("Trading Bot").Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[grey]ADX Trend Following Strategy with Risk Management[/]");
        AnsiConsole.MarkupLine("[grey]Based on research: target Sharpe 1.5-1.9, max DD <20%[/]\n");

        var mode = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select [green]mode[/]:")
                .AddChoices(
                    "Backtest",
                    "Parameter Optimization",
                    "Walk-Forward Analysis",
                    "Monte Carlo Simulation",
                    "Live Trading (Paper)",
                    "Live Trading (Real)",
                    "Download Data",
                    "Configuration Settings",
                    "Reset to Defaults",
                    "Exit")
        );

        switch (mode)
        {
            case "Backtest":
                await RunBacktest();
                break;
            case "Parameter Optimization":
                await RunOptimization();
                break;
            case "Walk-Forward Analysis":
                await RunWalkForward();
                break;
            case "Monte Carlo Simulation":
                await RunMonteCarlo();
                break;
            case "Live Trading (Paper)":
                await RunLiveTrading(paperTrade: true);
                break;
            case "Live Trading (Real)":
                await RunLiveTrading(paperTrade: false);
                break;
            case "Download Data":
                await DownloadData();
                break;
            case "Configuration Settings":
                ConfigureSettings();
                break;
            case "Reset to Defaults":
                _configService.ResetToDefaults();
                break;
        }
    }

    static async Task RunBacktest()
    {
        var (candles, symbol) = await LoadData();
        if (candles.Count == 0) return;

        var riskSettings = GetRiskSettings();
        var strategySettings = GetStrategySettings();
        var backtestSettings = new BacktestSettings
        {
            InitialCapital = AnsiConsole.Ask("Initial capital [green](USDT)[/]:", 10000m),
            CommissionPercent = 0.1m
        };

        var strategy = SelectStrategy(strategySettings);
        var journal = new TradeJournal();
        var engine = new BacktestEngine(strategy, riskSettings, backtestSettings, journal);

        BacktestResult result = null!;
        await AnsiConsole.Status()
            .StartAsync("Running backtest...", async ctx =>
            {
                result = engine.Run(candles, symbol);
                await Task.CompletedTask;
            });

        DisplayBacktestResults(result);

        // Export trade journal
        if (AnsiConsole.Confirm("Export trade journal to CSV?", defaultValue: true))
        {
            journal.ExportToCsv();
            var stats = journal.GetStats();
            AnsiConsole.MarkupLine($"\n[green]Trade Journal Statistics:[/]");
            AnsiConsole.MarkupLine($"  Total Trades: {stats.TotalTrades}");
            AnsiConsole.MarkupLine($"  Win Rate: {stats.WinRate:F1}%");
            AnsiConsole.MarkupLine($"  Average R-Multiple: {stats.AverageRMultiple:F2}");
            AnsiConsole.MarkupLine($"  Total Net P&L: ${stats.TotalNetPnL:F2}");
            AnsiConsole.MarkupLine($"  Average Win: ${stats.AverageWin:F2}");
            AnsiConsole.MarkupLine($"  Average Loss: ${stats.AverageLoss:F2}");
            AnsiConsole.MarkupLine($"  Largest Win: ${stats.LargestWin:F2}");
            AnsiConsole.MarkupLine($"  Largest Loss: ${stats.LargestLoss:F2}");
            AnsiConsole.MarkupLine($"  Average Bars in Trade: {stats.AverageBarsInTrade:F1}");
        }
    }

    static async Task RunOptimization()
    {
        var (candles, symbol) = await LoadData();
        if (candles.Count == 0) return;

        // Strategy selection for optimization
        var strategyChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select strategy to optimize:")
                .AddChoices(
                    "ADX Trend Following (Full Grid Search)",
                    "MA Crossover (Genetic)",
                    "RSI Mean Reversion (Genetic)",
                    "RSI Mean Reversion (Quick Test)",
                    "MA Crossover (Quick Test)",
                    "Strategy Ensemble (Weights Only)",
                    "Strategy Ensemble (Full - All Parameters)")
        );

        var riskSettings = GetRiskSettings();
        var backtestSettings = new BacktestSettings { InitialCapital = 10000m };

        if (strategyChoice == "Strategy Ensemble (Weights Only)")
        {
            var optimizeFor = PromptOptimizationTarget();
            await RunEnsembleOptimization(candles, symbol, riskSettings, backtestSettings, optimizeFor);
            return;
        }

        if (strategyChoice == "Strategy Ensemble (Full - All Parameters)")
        {
            var optimizeFor = PromptOptimizationTarget();
            await RunFullEnsembleOptimization(candles, symbol, riskSettings, backtestSettings, optimizeFor);
            return;
        }

        if (strategyChoice == "MA Crossover (Genetic)")
        {
            var optimizeFor = PromptOptimizationTarget();
            await RunMaOptimization(candles, symbol, riskSettings, backtestSettings, optimizeFor);
            return;
        }

        if (strategyChoice == "RSI Mean Reversion (Genetic)")
        {
            var optimizeFor = PromptOptimizationTarget();
            await RunRsiOptimization(candles, symbol, riskSettings, backtestSettings, optimizeFor);
            return;
        }

        if (strategyChoice != "ADX Trend Following (Full Grid Search)")
        {
            // Quick test for other strategies - run single backtest
            IStrategy strategy = strategyChoice switch
            {
                "RSI Mean Reversion (Quick Test)" => new RsiStrategy(),
                "MA Crossover (Quick Test)" => new MaStrategy(),
                _ => new AdxTrendStrategy()
            };

            AnsiConsole.MarkupLine($"\n[yellow]Running backtest for {strategy.Name}...[/]");
            var journal = new TradeJournal();
            var engine = new BacktestEngine(strategy, riskSettings, backtestSettings, journal);
            var btResult = engine.Run(candles, symbol);
            DisplayBacktestResults(btResult);
            return;
        }

        // ADX Optimization
        AnsiConsole.MarkupLine("\n[yellow]ADX Parameter Ranges (Grid Search)[/]");

        var adxOptimizeFor = PromptOptimizationTarget();

        var useDefaultRanges = AnsiConsole.Confirm("Use default parameter ranges?", true);

        OptimizerSettings optimizerSettings;
        if (useDefaultRanges)
        {
            optimizerSettings = new OptimizerSettings { OptimizeFor = adxOptimizeFor };
        }
        else
        {
            optimizerSettings = new OptimizerSettings
            {
                OptimizeFor = adxOptimizeFor,
                AdxPeriodRange = ParseIntRange(AnsiConsole.Ask("ADX periods [green](comma-separated)[/]:", "10,14,20")),
                AdxThresholdRange = ParseDecimalRange(AnsiConsole.Ask("ADX thresholds:", "20,25,30")),
                FastEmaRange = ParseIntRange(AnsiConsole.Ask("Fast EMA periods:", "10,15,20,25")),
                SlowEmaRange = ParseIntRange(AnsiConsole.Ask("Slow EMA periods:", "40,50,60,80")),
                AtrMultiplierRange = ParseDecimalRange(AnsiConsole.Ask("ATR multipliers:", "2.0,2.5,3.0")),
                VolumeThresholdRange = ParseDecimalRange(AnsiConsole.Ask("Volume thresholds:", "1.0,1.5,2.0"))
            };
        }

        var optimizer = new ParameterOptimizer(optimizerSettings);
        
        // Calculate total combinations
        int totalCombos = optimizerSettings.AdxPeriodRange.Length 
            * optimizerSettings.AdxThresholdRange.Length
            * optimizerSettings.FastEmaRange.Length
            * optimizerSettings.SlowEmaRange.Length
            * optimizerSettings.AtrMultiplierRange.Length
            * optimizerSettings.VolumeThresholdRange.Length;

        AnsiConsole.MarkupLine($"\n[grey]Testing ~{totalCombos} parameter combinations...[/]\n");

        OptimizationResult result = null!;
        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Optimizing parameters...");
                var progress = new Progress<OptimizationProgress>(p => 
                {
                    task.Value = p.PercentComplete;
                });
                
                result = optimizer.Optimize(candles, symbol, riskSettings, backtestSettings, progress);
                await Task.CompletedTask;
            });

        DisplayOptimizationResults(result);
    }

    static int[] ParseIntRange(string input) => 
        input.Split(',').Select(s => int.Parse(s.Trim())).ToArray();

    static decimal[] ParseDecimalRange(string input) => 
        input.Split(',').Select(s => decimal.Parse(s.Trim())).ToArray();

    static OptimizationTarget PromptOptimizationTarget() =>
        AnsiConsole.Prompt(
            new SelectionPrompt<OptimizationTarget>()
                .Title("Optimize for:")
                .AddChoices(
                    OptimizationTarget.RiskAdjusted,
                    OptimizationTarget.SharpeRatio,
                    OptimizationTarget.SortinoRatio,
                    OptimizationTarget.ProfitFactor,
                    OptimizationTarget.TotalReturn
                )
        );

    static FitnessFunction ToFitnessFunction(OptimizationTarget target) => target switch
    {
        OptimizationTarget.SharpeRatio => FitnessFunction.Sharpe,
        OptimizationTarget.SortinoRatio => FitnessFunction.Sortino,
        OptimizationTarget.ProfitFactor => FitnessFunction.ProfitFactor,
        OptimizationTarget.TotalReturn => FitnessFunction.Return,
        OptimizationTarget.RiskAdjusted => FitnessFunction.RiskAdjusted,
        _ => FitnessFunction.RiskAdjusted
    };

    static async Task RunEnsembleOptimization(
        List<Candle> candles,
        string symbol,
        RiskSettings riskSettings,
        BacktestSettings backtestSettings,
        OptimizationTarget optimizeFor)
    {
        var config = _configService.GetConfiguration();
        var optimizerConfig = config.EnsembleOptimizer.ToEnsembleOptimizerConfig();
        var geneticSettings = config.GeneticOptimizer.ToGeneticOptimizerSettings();
        var optimizer = new EnsembleStrategyOptimizer(optimizerConfig, riskSettings, backtestSettings, optimizeFor);

        GeneticOptimizationResult<EnsembleOptimizationSettings> result = null!;
        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Optimizing ensemble...");
                var progress = new Progress<GeneticProgress<EnsembleOptimizationSettings>>(p =>
                {
                    task.Description = $"Optimizing ensemble... Gen {p.CurrentGeneration}/{p.TotalGenerations}";
                    task.Value = p.PercentComplete;
                });

                result = optimizer.Optimize(candles, symbol, geneticSettings, progress);
                await Task.CompletedTask;
            });

        var bestSettings = result.BestSettings;
        var ensembleSettings = bestSettings.ToEnsembleSettings();
        var strategy = StrategyEnsemble.CreateDefault(ensembleSettings);
        var engine = new BacktestEngine(strategy, riskSettings, backtestSettings);
        var backtestResult = engine.Run(candles, symbol);

        DisplayEnsembleOptimizationResults(result, backtestResult);
        PromptSaveEnsembleSettings(bestSettings);
    }

    static async Task RunFullEnsembleOptimization(
        List<Candle> candles,
        string symbol,
        RiskSettings riskSettings,
        BacktestSettings backtestSettings,
        OptimizationTarget optimizeFor)
    {
        var config = _configService.GetConfiguration();
        var geneticSettings = config.GeneticOptimizer.ToGeneticOptimizerSettings();

        // Use larger population and more generations for the bigger search space
        geneticSettings = geneticSettings with
        {
            PopulationSize = Math.Max(geneticSettings.PopulationSize, 150),
            Generations = Math.Max(geneticSettings.Generations, 80)
        };

        var optimizer = new FullEnsembleOptimizer(
            riskSettings: riskSettings,
            backtestSettings: backtestSettings,
            optimizeFor: optimizeFor);

        AnsiConsole.MarkupLine($"\n[yellow]Full Ensemble Optimization (~25 parameters)[/]");
        AnsiConsole.MarkupLine($"[grey]Population: {geneticSettings.PopulationSize}, Generations: {geneticSettings.Generations}[/]");
        AnsiConsole.MarkupLine("[grey]This may take a while...[/]\n");

        GeneticOptimizationResult<FullEnsembleSettings> result = null!;
        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Optimizing full ensemble...");
                var progress = new Progress<GeneticProgress<FullEnsembleSettings>>(p =>
                {
                    task.Description = $"Optimizing full ensemble... Gen {p.CurrentGeneration}/{p.TotalGenerations} (Best: {p.BestFitness:F2})";
                    task.Value = p.PercentComplete;
                });

                result = optimizer.Optimize(candles, symbol, geneticSettings, progress);
                await Task.CompletedTask;
            });

        var bestSettings = result.BestSettings;
        var strategy = FullEnsembleOptimizer.CreateEnsembleFromSettings(bestSettings);
        var engine = new BacktestEngine(strategy, riskSettings, backtestSettings);
        var backtestResult = engine.Run(candles, symbol);

        DisplayFullEnsembleResults(result, backtestResult);
    }

    static void DisplayFullEnsembleResults(
        GeneticOptimizationResult<FullEnsembleSettings> result,
        BacktestResult backtestResult)
    {
        var best = result.BestSettings;

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Full Ensemble Optimization Results[/]").RuleStyle("grey"));

        // Weights table
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

        // ADX parameters
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

        // MA parameters
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

        // RSI parameters
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

    static async Task RunMaOptimization(
        List<Candle> candles,
        string symbol,
        RiskSettings riskSettings,
        BacktestSettings backtestSettings,
        OptimizationTarget optimizeFor)
    {
        var config = _configService.GetConfiguration();
        var optimizerConfig = config.MaOptimizer.ToMaOptimizerConfig();
        var geneticSettings = config.GeneticOptimizer.ToGeneticOptimizerSettings();
        var fitness = ToFitnessFunction(optimizeFor);
        var optimizer = new MaStrategyOptimizer(optimizerConfig, riskSettings, backtestSettings, fitness);

        GeneticOptimizationResult<MaStrategySettings> result = null!;
        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Optimizing MA...");
                var progress = new Progress<GeneticProgress<MaStrategySettings>>(p =>
                {
                    task.Description = $"Optimizing MA... Gen {p.CurrentGeneration}/{p.TotalGenerations}";
                    task.Value = p.PercentComplete;
                });

                result = optimizer.Optimize(candles, symbol, geneticSettings, progress);
                await Task.CompletedTask;
            });

        var bestSettings = result.BestSettings;
        var strategy = new MaStrategy(bestSettings);
        var engine = new BacktestEngine(strategy, riskSettings, backtestSettings);
        var backtestResult = engine.Run(candles, symbol);

        DisplayMaOptimizationResults(result, backtestResult);
        PromptSaveMaSettings(bestSettings);
    }

    static async Task RunRsiOptimization(
        List<Candle> candles,
        string symbol,
        RiskSettings riskSettings,
        BacktestSettings backtestSettings,
        OptimizationTarget optimizeFor)
    {
        var config = _configService.GetConfiguration();
        var optimizerConfig = config.RsiOptimizer.ToRsiOptimizerConfig();
        var geneticSettings = config.GeneticOptimizer.ToGeneticOptimizerSettings();
        var fitness = ToFitnessFunction(optimizeFor);
        var optimizer = new RsiStrategyOptimizer(optimizerConfig, riskSettings, backtestSettings, fitness);

        GeneticOptimizationResult<RsiStrategySettings> result = null!;
        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Optimizing RSI...");
                var progress = new Progress<GeneticProgress<RsiStrategySettings>>(p =>
                {
                    task.Description = $"Optimizing RSI... Gen {p.CurrentGeneration}/{p.TotalGenerations}";
                    task.Value = p.PercentComplete;
                });

                result = optimizer.Optimize(candles, symbol, geneticSettings, progress);
                await Task.CompletedTask;
            });

        var bestSettings = result.BestSettings;
        var strategy = new RsiStrategy(bestSettings);
        var engine = new BacktestEngine(strategy, riskSettings, backtestSettings);
        var backtestResult = engine.Run(candles, symbol);

        DisplayRsiOptimizationResults(result, backtestResult);
        PromptSaveRsiSettings(bestSettings);
    }

    static void DisplayOptimizationResults(OptimizationResult result)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Optimization Results[/]").RuleStyle("grey"));

        // Summary
        var summaryTable = new Table().Border(TableBorder.Rounded)
            .AddColumn("Metric").AddColumn("Value");
        
        summaryTable.AddRow("Total combinations", result.TotalCombinations.ToString());
        summaryTable.AddRow("Valid combinations", result.ValidCombinations.ToString());
        summaryTable.AddRow("In-Sample Period", $"{result.InSampleStart:yyyy-MM-dd} → {result.InSampleEnd:yyyy-MM-dd}");
        summaryTable.AddRow("Out-of-Sample Period", $"{result.OutOfSampleStart:yyyy-MM-dd} → {result.OutOfSampleEnd:yyyy-MM-dd}");
        summaryTable.AddRow("Robust Results", result.RobustResults.Count.ToString());
        
        AnsiConsole.Write(summaryTable);

        // Top results table
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

        // Best robust parameters
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

    static void DisplayEnsembleOptimizationResults(
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

    static void DisplayMaOptimizationResults(
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

    static void DisplayRsiOptimizationResults(
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

    static void PromptSaveEnsembleSettings(EnsembleOptimizationSettings settings)
    {
        if (!AnsiConsole.Confirm("Save best ensemble settings to appsettings.user.json?", defaultValue: true))
            return;

        var current = _configService.GetConfiguration().Ensemble;
        var updated = new EnsembleConfigSettings
        {
            Enabled = current.Enabled,
            MinimumAgreement = settings.MinimumAgreement,
            UseConfidenceWeighting = settings.UseConfidenceWeighting,
            StrategyWeights = new Dictionary<string, decimal>
            {
                ["ADX Trend Following + Volume"] = settings.AdxWeight,
                ["MA Crossover"] = settings.MaWeight,
                ["RSI Mean Reversion"] = settings.RsiWeight
            }
        };

        _configService.UpdateSection("Ensemble", updated);
    }

    static void PromptSaveMaSettings(MaStrategySettings settings)
    {
        if (!AnsiConsole.Confirm("Save best MA settings to appsettings.user.json?", defaultValue: true))
            return;

        var updated = MaStrategyConfigSettings.FromSettings(settings);
        _configService.UpdateSection("MaStrategy", updated);
    }

    static void PromptSaveRsiSettings(RsiStrategySettings settings)
    {
        if (!AnsiConsole.Confirm("Save best RSI settings to appsettings.user.json?", defaultValue: true))
            return;

        var updated = RsiStrategyConfigSettings.FromSettings(settings);
        _configService.UpdateSection("RsiStrategy", updated);
    }

    static async Task RunWalkForward()
    {
        var (candles, symbol) = await LoadData();
        if (candles.Count == 0) return;

        var riskSettings = GetRiskSettings();
        var strategySettings = GetStrategySettings();
        var backtestSettings = new BacktestSettings { InitialCapital = 10000m };

        var analyzer = new WalkForwardAnalyzer();
        
        WalkForwardResult result = null!;
        await AnsiConsole.Status()
            .StartAsync("Running walk-forward analysis...", async ctx =>
            {
                result = analyzer.Analyze(
                    candles,
                    symbol,
                    () => new AdxTrendStrategy(strategySettings),
                    riskSettings,
                    backtestSettings
                );
                await Task.CompletedTask;
            });

        DisplayWalkForwardResults(result);
    }

    static async Task RunMonteCarlo()
    {
        var (candles, symbol) = await LoadData();
        if (candles.Count == 0) return;

        var riskSettings = GetRiskSettings();
        var strategySettings = GetStrategySettings();
        var backtestSettings = new BacktestSettings { InitialCapital = 10000m };

        // First run backtest
        var strategy = new AdxTrendStrategy(strategySettings);
        var engine = new BacktestEngine(strategy, riskSettings, backtestSettings);
        var backtestResult = engine.Run(candles, symbol);

        // Then Monte Carlo
        int simulations = AnsiConsole.Ask("Number of simulations:", 1000);
        var simulator = new MonteCarloSimulator(simulations);

        MonteCarloResult result = null!;
        await AnsiConsole.Status()
            .StartAsync($"Running {simulations} Monte Carlo simulations...", async ctx =>
            {
                result = simulator.Simulate(backtestResult);
                await Task.CompletedTask;
            });

        DisplayMonteCarloResults(result, backtestResult);
    }

    static async Task DownloadData()
    {
        var symbol = AnsiConsole.Ask("Symbol:", "BTCUSDT");
        var interval = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Interval:")
                .AddChoices("1h", "4h", "1d")
        );
        
        var startDate = AnsiConsole.Ask("Start date [green](yyyy-MM-dd)[/]:", 
            DateTime.UtcNow.AddYears(-1).ToString("yyyy-MM-dd"));
        var endDate = AnsiConsole.Ask("End date [green](yyyy-MM-dd)[/]:", 
            DateTime.UtcNow.ToString("yyyy-MM-dd"));

        var loader = new HistoricalDataLoader();
        var candles = new List<Candle>();

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"Downloading {symbol} data...");
                var progress = new Progress<int>(p => task.Value = p);
                
                candles = await loader.LoadAsync(
                    symbol,
                    KlineIntervalExtensions.Parse(interval),
                    DateTime.Parse(startDate),
                    DateTime.Parse(endDate),
                    progress
                );
            });

        var filename = $"Data/{symbol}_{interval}_{startDate}_{endDate}.csv";
        Directory.CreateDirectory("Data");
        await loader.SaveToCsvAsync(candles, filename);
        
        AnsiConsole.MarkupLine($"\n[green]✓[/] Saved {candles.Count} candles to [blue]{filename}[/]");
    }

    static async Task RunLiveTrading(bool paperTrade)
    {
        if (!paperTrade)
        {
            var confirm = AnsiConsole.Prompt(
                new ConfirmationPrompt("[red]⚠️ REAL MONEY MODE - Are you absolutely sure?[/]")
                { DefaultValue = false }
            );
            if (!confirm) return;
        }

        AnsiConsole.MarkupLine($"\n[yellow]═══ {(paperTrade ? "PAPER" : "LIVE")} TRADING MODE ═══[/]\n");

        // Load API credentials from config
        var config = _configService.GetConfiguration();
        var apiKey = config.BinanceApi.ApiKey;
        var apiSecret = config.BinanceApi.ApiSecret;
        var useTestnet = config.BinanceApi.UseTestnet;

        // Validate API keys
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
        {
            AnsiConsole.MarkupLine("[red]✗ API keys not configured![/]");
            AnsiConsole.MarkupLine("[yellow]Please configure API keys via:[/] Configuration Settings → API Keys");
            if (useTestnet)
            {
                AnsiConsole.MarkupLine("[grey]Get testnet keys at: https://testnet.binance.vision/[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[grey]Get API keys at: https://www.binance.com/en/my/settings/api-management[/]");
            }
            return;
        }

        AnsiConsole.MarkupLine($"[grey]Using {(useTestnet ? "Testnet" : "Live")} API keys from configuration[/]\n");

        var symbol = AnsiConsole.Ask("Symbol:", config.LiveTrading.Symbol);
        var interval = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"Interval (current: [green]{config.LiveTrading.Interval}[/]):")
                .AddChoices("1h", "4h", "1d")
        );
        var tradingMode = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"Trading mode (current: [green]{config.LiveTrading.TradingMode}[/]):")
                .AddChoices("Spot (no margin)", "Futures/Margin")
        );

        var riskSettings = GetRiskSettings();
        var strategySettings = GetStrategySettings();
        var initialCapital = AnsiConsole.Ask($"Initial capital [green](USDT)[/] [{config.LiveTrading.InitialCapital}]:", config.LiveTrading.InitialCapital);

        // Telegram configuration from config
        TelegramNotifier? telegram = null;
        if (config.Telegram.Enabled && !string.IsNullOrWhiteSpace(config.Telegram.BotToken))
        {
            telegram = new TelegramNotifier(config.Telegram.BotToken, config.Telegram.ChatId);
            AnsiConsole.MarkupLine("[green]✓[/] Telegram notifications enabled (from config)\n");
        }
        else if (AnsiConsole.Confirm("Enable Telegram notifications?", defaultValue: false))
        {
            AnsiConsole.MarkupLine("[yellow]Please configure Telegram via:[/] Configuration Settings → Telegram Notifications");
        }

        var liveSettings = new LiveTraderSettings
        {
            Symbol = symbol,
            Interval = KlineIntervalExtensions.Parse(interval),
            InitialCapital = initialCapital,
            UseTestnet = useTestnet,
            PaperTrade = paperTrade,
            TradingMode = tradingMode == "Spot (no margin)" ? TradingMode.Spot : TradingMode.Futures
        };

        var strategy = new AdxTrendStrategy(strategySettings);
        using var trader = new BinanceLiveTrader(
            apiKey, apiSecret, strategy, riskSettings, liveSettings, telegram);

        // Setup signal display
        var signalTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Time")
            .AddColumn("Signal")
            .AddColumn("Price")
            .AddColumn("Reason");

        trader.OnSignal += signal =>
        {
            signalTable.AddRow(
                DateTime.UtcNow.ToString("HH:mm:ss"),
                signal.Type.ToString(),
                $"{signal.Price:F2}",
                signal.Reason ?? ""
            );
        };

        AnsiConsole.MarkupLine("\n[green]Starting trader... Press Ctrl+C to stop[/]\n");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await trader.StartAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled
        }

        await trader.StopAsync();
        
        if (signalTable.Rows.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[yellow]Signals Generated[/]"));
            AnsiConsole.Write(signalTable);
        }
    }

    static async Task<(List<Candle> candles, string symbol)> LoadData()
    {
        var source = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Data source:")
                .AddChoices("Download from Binance", "Load from CSV file")
        );

        if (source == "Load from CSV file")
        {
            var files = Directory.Exists("Data") 
                ? Directory.GetFiles("Data", "*.csv") 
                : Array.Empty<string>();

            if (files.Length == 0)
            {
                AnsiConsole.MarkupLine("[red]No CSV files found in Data folder[/]");
                return (new List<Candle>(), "");
            }

            var file = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select file:")
                    .AddChoices(files.Select(Path.GetFileName).Where(f => f != null)!)
            );

            var loader = new HistoricalDataLoader();
            var candles = await loader.LoadFromCsvAsync($"Data/{file}");
            var symbol = file!.Split('_')[0];
            
            AnsiConsole.MarkupLine($"[green]✓[/] Loaded {candles.Count} candles");
            return (candles, symbol);
        }
        else
        {
            var symbol = AnsiConsole.Ask("Symbol:", "BTCUSDT");
            var interval = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Interval (recommended [green]4h[/] or [green]1d[/] for medium-term):")
                    .AddChoices("1h", "4h", "1d")
            );

            var months = AnsiConsole.Ask("History months:", 12);
            var loader = new HistoricalDataLoader();
            var candles = new List<Candle>();

            await AnsiConsole.Progress()
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask($"Downloading {symbol}...");
                    var progress = new Progress<int>(p => task.Value = p);

                    candles = await loader.LoadAsync(
                        symbol,
                        KlineIntervalExtensions.Parse(interval),
                        DateTime.UtcNow.AddMonths(-months),
                        DateTime.UtcNow,
                        progress
                    );
                });

            AnsiConsole.MarkupLine($"[green]✓[/] Downloaded {candles.Count} candles");
            return (candles, symbol);
        }
    }

    static RiskSettings GetRiskSettings()
    {
        var config = _configService.GetConfiguration();
        var current = config.RiskManagement;

        var useDefaults = AnsiConsole.Confirm("Use saved risk settings?", defaultValue: true);

        if (useDefaults)
            return current.ToRiskSettings();

        AnsiConsole.MarkupLine("\n[yellow]Risk Management Settings[/]");
        AnsiConsole.MarkupLine("[grey]Press Enter to keep current value shown in brackets[/]\n");

        var updated = new RiskManagementSettings
        {
            RiskPerTradePercent = AnsiConsole.Ask($"Risk per trade [green](%)[/] [[{current.RiskPerTradePercent}]]:", current.RiskPerTradePercent),
            MaxPortfolioHeatPercent = AnsiConsole.Ask($"Max portfolio heat [green](%)[/] [[{current.MaxPortfolioHeatPercent}]]:", current.MaxPortfolioHeatPercent),
            MaxDrawdownPercent = AnsiConsole.Ask($"Max drawdown circuit breaker [green](%)[/] [[{current.MaxDrawdownPercent}]]:", current.MaxDrawdownPercent),
            MaxDailyDrawdownPercent = AnsiConsole.Ask($"Max daily drawdown [green](%)[/] [[{current.MaxDailyDrawdownPercent}]]:", current.MaxDailyDrawdownPercent),
            AtrStopMultiplier = AnsiConsole.Ask($"ATR stop multiplier [[{current.AtrStopMultiplier}]]:", current.AtrStopMultiplier),
            TakeProfitMultiplier = AnsiConsole.Ask($"Take profit ratio (reward:risk) [[{current.TakeProfitMultiplier}]]:", current.TakeProfitMultiplier),
            MinimumEquityUsd = AnsiConsole.Ask($"Minimum equity USD [[{current.MinimumEquityUsd}]]:", current.MinimumEquityUsd)
        };

        if (AnsiConsole.Confirm("Save these settings?", defaultValue: true))
        {
            _configService.UpdateSection("RiskManagement", updated);
        }

        return updated.ToRiskSettings();
    }

    static StrategySettings GetStrategySettings()
    {
        var config = _configService.GetConfiguration();
        var current = config.Strategy;

        var useDefaults = AnsiConsole.Confirm("Use saved strategy settings?", defaultValue: true);

        if (useDefaults)
            return current.ToStrategySettings();

        AnsiConsole.MarkupLine("\n[yellow]Strategy Settings[/]");
        AnsiConsole.MarkupLine("[grey]Press Enter to keep current value shown in brackets[/]\n");

        var updated = new StrategyConfigSettings
        {
            AdxPeriod = AnsiConsole.Ask($"ADX period [[{current.AdxPeriod}]]:", current.AdxPeriod),
            AdxThreshold = AnsiConsole.Ask($"ADX entry threshold [[{current.AdxThreshold}]]:", current.AdxThreshold),
            AdxExitThreshold = AnsiConsole.Ask($"ADX exit threshold [[{current.AdxExitThreshold}]]:", current.AdxExitThreshold),
            RequireAdxRising = AnsiConsole.Confirm("Require ADX rising?", current.RequireAdxRising),
            AdxSlopeLookback = AnsiConsole.Ask($"ADX slope lookback (bars) [[{current.AdxSlopeLookback}]]:", current.AdxSlopeLookback),
            FastEmaPeriod = AnsiConsole.Ask($"Fast EMA period [[{current.FastEmaPeriod}]]:", current.FastEmaPeriod),
            SlowEmaPeriod = AnsiConsole.Ask($"Slow EMA period [[{current.SlowEmaPeriod}]]:", current.SlowEmaPeriod),
            AtrPeriod = AnsiConsole.Ask($"ATR period [[{current.AtrPeriod}]]:", current.AtrPeriod),
            MinAtrPercent = AnsiConsole.Ask($"Min ATR % of price [[{current.MinAtrPercent}]]:", current.MinAtrPercent),
            MaxAtrPercent = AnsiConsole.Ask($"Max ATR % of price [[{current.MaxAtrPercent}]]:", current.MaxAtrPercent),
            RequireVolumeConfirmation = AnsiConsole.Confirm("Require volume confirmation?", current.RequireVolumeConfirmation),
            VolumeThreshold = AnsiConsole.Ask($"Volume spike threshold (x avg) [[{current.VolumeThreshold}]]:", current.VolumeThreshold),
            RequireObvConfirmation = AnsiConsole.Confirm("Require OBV confirmation?", current.RequireObvConfirmation)
        };

        // Copy remaining fields from current config
        updated.RequireFreshTrend = current.RequireFreshTrend;
        updated.AdxFallingExitBars = current.AdxFallingExitBars;
        updated.MaxBarsInTrade = current.MaxBarsInTrade;
        updated.AtrStopMultiplier = current.AtrStopMultiplier;
        updated.TakeProfitMultiplier = current.TakeProfitMultiplier;
        updated.VolumePeriod = current.VolumePeriod;
        updated.ObvPeriod = current.ObvPeriod;
        updated.PartialExitRMultiple = current.PartialExitRMultiple;
        updated.PartialExitFraction = current.PartialExitFraction;

        if (AnsiConsole.Confirm("Save these settings?", defaultValue: true))
        {
            _configService.UpdateSection("Strategy", updated);
        }

        return updated.ToStrategySettings();
    }

    static IStrategy SelectStrategy(StrategySettings? adxSettings = null)
    {
        var ensembleSettings = _configService.GetConfiguration()
            .Ensemble
            .ToEnsembleSettings();

        var strategyChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select [green]strategy[/]:")
                .AddChoices(
                    "ADX Trend Following (Recommended)",
                    "RSI Mean Reversion",
                    "MA Crossover",
                    "Strategy Ensemble (All Combined)")
        );

        return strategyChoice switch
        {
            "ADX Trend Following (Recommended)" => new AdxTrendStrategy(adxSettings),
            "RSI Mean Reversion" => new RsiStrategy(),
            "MA Crossover" => new MaStrategy(),
            "Strategy Ensemble (All Combined)" => StrategyEnsemble.CreateDefault(ensembleSettings),
            _ => new AdxTrendStrategy(adxSettings)
        };
    }

    static void DisplayBacktestResults(BacktestResult result)
    {
        var m = result.Metrics;
        
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Backtest Results[/]").RuleStyle("grey"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn("Value");

        // Performance
        table.AddRow("Strategy", result.StrategyName);
        table.AddRow("Period", $"{result.StartDate:yyyy-MM-dd} → {result.EndDate:yyyy-MM-dd}");
        table.AddRow("Initial Capital", $"${result.InitialCapital:N2}");
        table.AddRow("Final Capital", FormatValue(result.FinalCapital, result.InitialCapital));
        table.AddRow("Total Return", FormatPercent(m.TotalReturn));
        table.AddRow("Annualized Return", FormatPercent(m.AnnualizedReturn));
        
        table.AddEmptyRow();
        
        // Risk metrics
        table.AddRow("[yellow]Risk Metrics[/]", "");
        table.AddRow("Max Drawdown", $"[red]-{m.MaxDrawdownPercent:F2}%[/]");
        table.AddRow("Sharpe Ratio", FormatRatio(m.SharpeRatio));
        table.AddRow("Sortino Ratio", FormatRatio(m.SortinoRatio));
        table.AddRow("Profit Factor", FormatRatio(m.ProfitFactor));
        
        table.AddEmptyRow();
        
        // Trade statistics
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

        // Strategy assessment
        var assessment = GetStrategyAssessment(m);
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(assessment.message)
            .Header($"[{assessment.color}]Assessment[/]")
            .Border(BoxBorder.Rounded));
    }

    static void DisplayWalkForwardResults(WalkForwardResult result)
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

        // Period details
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

    static void DisplayMonteCarloResults(MonteCarloResult result, BacktestResult backtest)
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

    static string FormatPercent(decimal value) =>
        value >= 0 ? $"[green]+{value:F2}%[/]" : $"[red]{value:F2}%[/]";

    static string FormatValue(decimal value, decimal reference) =>
        value >= reference ? $"[green]${value:N2}[/]" : $"[red]${value:N2}[/]";

    static string FormatRatio(decimal value) =>
        value switch
        {
            >= 2 => $"[green]{value:F2}[/]",
            >= 1 => $"[yellow]{value:F2}[/]",
            _ => $"[red]{value:F2}[/]"
        };

    static string FormatWfe(decimal value) =>
        value switch
        {
            >= 70 => $"[green]{value:F1}%[/]",
            >= 50 => $"[yellow]{value:F1}%[/]",
            _ => $"[red]{value:F1}%[/]"
        };

    static string FormatRuinProb(decimal value) =>
        value switch
        {
            < 1 => $"[green]{value:F2}%[/]",
            < 5 => $"[yellow]{value:F2}%[/]",
            _ => $"[red]{value:F2}%[/]"
        };

    static (string color, string message) GetStrategyAssessment(PerformanceMetrics m)
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

    static void ConfigureSettings()
    {
        var section = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Which settings to configure?")
                .AddChoices(
                    "Risk Management",
                    "Strategy Parameters",
                    "Correlation Groups",
                    "Telegram Notifications",
                    "API Keys",
                    "Back to Menu")
        );

        switch (section)
        {
            case "Risk Management":
                _configService.EditInteractive("risk");
                break;
            case "Strategy Parameters":
                _configService.EditInteractive("strategy");
                break;
            case "Correlation Groups":
                _configService.EditInteractive("correlation");
                break;
            case "Telegram Notifications":
                _configService.EditInteractive("telegram");
                break;
            case "API Keys":
                _configService.EditInteractive("api");
                break;
        }
    }
}
