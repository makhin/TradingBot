using Binance.Net.Enums;
using Spectre.Console;
using ComplexBot.Models;
using ComplexBot.Services.Backtesting;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.Trading;

namespace ComplexBot;

class Program
{
    static async Task Main(string[] args)
    {
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

        var strategy = new AdxTrendStrategy(strategySettings);
        var engine = new BacktestEngine(strategy, riskSettings, backtestSettings);

        BacktestResult result = null!;
        await AnsiConsole.Status()
            .StartAsync("Running backtest...", async ctx =>
            {
                result = engine.Run(candles, symbol);
                await Task.CompletedTask;
            });

        DisplayBacktestResults(result);
    }

    static async Task RunOptimization()
    {
        var (candles, symbol) = await LoadData();
        if (candles.Count == 0) return;

        var riskSettings = GetRiskSettings();
        var backtestSettings = new BacktestSettings { InitialCapital = 10000m };

        // Optimization settings
        AnsiConsole.MarkupLine("\n[yellow]Parameter Ranges (Grid Search)[/]");
        
        var optimizeFor = AnsiConsole.Prompt(
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

        var useDefaultRanges = AnsiConsole.Confirm("Use default parameter ranges?", true);
        
        OptimizerSettings optimizerSettings;
        if (useDefaultRanges)
        {
            optimizerSettings = new OptimizerSettings { OptimizeFor = optimizeFor };
        }
        else
        {
            optimizerSettings = new OptimizerSettings
            {
                OptimizeFor = optimizeFor,
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

        // API credentials
        var useTestnet = AnsiConsole.Confirm("Use Binance Testnet?", true);
        
        string apiKey, apiSecret;
        if (useTestnet)
        {
            AnsiConsole.MarkupLine("[grey]Get testnet keys at: https://testnet.binance.vision/[/]");
            apiKey = AnsiConsole.Ask<string>("Testnet API Key:");
            apiSecret = AnsiConsole.Prompt(new TextPrompt<string>("Testnet API Secret:").Secret());
        }
        else
        {
            apiKey = AnsiConsole.Ask<string>("API Key:");
            apiSecret = AnsiConsole.Prompt(new TextPrompt<string>("API Secret:").Secret());
        }

        var symbol = AnsiConsole.Ask("Symbol:", "BTCUSDT");
        var interval = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Interval (recommended [green]4h[/] for medium-term):")
                .AddChoices("1h", "4h", "1d")
        );
        var tradingMode = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Trading mode:")
                .AddChoices("Spot (no margin)", "Futures/Margin")
        );

        var riskSettings = GetRiskSettings();
        var strategySettings = GetStrategySettings();
        var initialCapital = AnsiConsole.Ask("Initial capital [green](USDT)[/]:", 10000m);

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
            apiKey, apiSecret, strategy, riskSettings, liveSettings);

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
        AnsiConsole.MarkupLine("\n[yellow]Risk Management Settings[/]");
        
        return new RiskSettings
        {
            RiskPerTradePercent = AnsiConsole.Ask("Risk per trade [green](%)[/]:", 1.5m),
            MaxPortfolioHeatPercent = AnsiConsole.Ask("Max portfolio heat [green](%)[/]:", 15m),
            MaxDrawdownPercent = AnsiConsole.Ask("Max drawdown circuit breaker [green](%)[/]:", 20m),
            AtrStopMultiplier = AnsiConsole.Ask("ATR stop multiplier:", 2.5m),
            TakeProfitMultiplier = AnsiConsole.Ask("Take profit ratio (reward:risk):", 1.5m)
        };
    }

    static StrategySettings GetStrategySettings()
    {
        var useDefaults = AnsiConsole.Confirm("Use default strategy settings?", true);
        
        if (useDefaults)
            return new StrategySettings();

        AnsiConsole.MarkupLine("\n[yellow]Strategy Settings[/]");
        
        return new StrategySettings
        {
            AdxPeriod = AnsiConsole.Ask("ADX period:", 14),
            AdxThreshold = AnsiConsole.Ask("ADX entry threshold:", 25m),
            AdxExitThreshold = AnsiConsole.Ask("ADX exit threshold:", 18m),
            FastEmaPeriod = AnsiConsole.Ask("Fast EMA period:", 20),
            SlowEmaPeriod = AnsiConsole.Ask("Slow EMA period:", 50),
            AtrPeriod = AnsiConsole.Ask("ATR period:", 14),
            RequireVolumeConfirmation = AnsiConsole.Confirm("Require volume confirmation?", true),
            VolumeThreshold = AnsiConsole.Ask("Volume spike threshold (x avg):", 1.5m),
            RequireObvConfirmation = AnsiConsole.Confirm("Require OBV confirmation?", true)
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
}
