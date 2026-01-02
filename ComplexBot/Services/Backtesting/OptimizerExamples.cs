using ComplexBot.Models;
using ComplexBot.Services.Strategies;
using ComplexBot.Services.RiskManagement;

namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Examples showing how to use generic GeneticOptimizer with different strategies
/// Demonstrates the delegate pattern for strategy-specific optimization
/// </summary>
public static class OptimizerExamples
{
    /// <summary>
    /// Example 1: Optimize ADX strategy using AdxStrategyOptimizer helper
    /// </summary>
    public static void OptimizeAdxStrategy()
    {
        // Load candle data (example)
        var candles = new List<Candle>(); // Load from DataLoader
        var symbol = "BTCUSDT";

        // Create ADX-specific optimizer with default config
        var adxOptimizer = new AdxStrategyOptimizer();

        // Run optimization
        var settings = new GeneticOptimizerSettings
        {
            PopulationSize = 100,
            Generations = 50
        };

        var result = adxOptimizer.Optimize(
            candles,
            symbol,
            settings,
            progress: new Progress<GeneticProgress<StrategySettings>>(p =>
            {
                Console.WriteLine($"Generation {p.CurrentGeneration}/{p.TotalGenerations}: " +
                    $"Best={p.BestFitness:F2}, Avg={p.AverageFitness:F2}");
            })
        );

        Console.WriteLine($"Best settings found: {result.BestSettings}");
        Console.WriteLine($"Best fitness: {result.BestFitness:F2}");
    }

    /// <summary>
    /// Example 2: Optimize MA strategy using manual delegate setup
    /// Shows how to create optimizer for any strategy type
    /// </summary>
    public static void OptimizeMaStrategy(
        List<Candle> candles,
        string symbol,
        RiskSettings riskSettings,
        BacktestSettings backtestSettings)
    {
        var random = new Random();

        // Define delegates for MA strategy optimization
        Func<MaStrategySettings> createRandom = () => new MaStrategySettings
        {
            FastMaPeriod = random.Next(5, 20),
            SlowMaPeriod = random.Next(25, 100),
            AtrPeriod = 14,
            AtrStopMultiplier = 1.5m + (decimal)random.NextDouble() * 2.5m,
            TakeProfitMultiplier = 1.0m + (decimal)random.NextDouble() * 2.0m,
            VolumeThreshold = 1.0m + (decimal)random.NextDouble() * 1.5m,
            RequireVolumeConfirmation = random.NextDouble() > 0.5
        };

        Func<MaStrategySettings, MaStrategySettings> mutate = settings =>
        {
            var param = random.Next(6);
            return param switch
            {
                0 => settings with { FastMaPeriod = Mutate(settings.FastMaPeriod, 5, 20, random) },
                1 => settings with { SlowMaPeriod = Mutate(settings.SlowMaPeriod, 25, 100, random) },
                2 => settings with { AtrStopMultiplier = Mutate(settings.AtrStopMultiplier, 1.5m, 4.0m, random) },
                3 => settings with { TakeProfitMultiplier = Mutate(settings.TakeProfitMultiplier, 1.0m, 3.0m, random) },
                4 => settings with { VolumeThreshold = Mutate(settings.VolumeThreshold, 1.0m, 2.5m, random) },
                _ => settings with { RequireVolumeConfirmation = !settings.RequireVolumeConfirmation }
            };
        };

        Func<MaStrategySettings, MaStrategySettings, MaStrategySettings> crossover = (p1, p2) => new MaStrategySettings
        {
            FastMaPeriod = Pick(p1.FastMaPeriod, p2.FastMaPeriod, random),
            SlowMaPeriod = Pick(p1.SlowMaPeriod, p2.SlowMaPeriod, random),
            AtrPeriod = 14,
            AtrStopMultiplier = Pick(p1.AtrStopMultiplier, p2.AtrStopMultiplier, random),
            TakeProfitMultiplier = Pick(p1.TakeProfitMultiplier, p2.TakeProfitMultiplier, random),
            VolumeThreshold = Pick(p1.VolumeThreshold, p2.VolumeThreshold, random),
            RequireVolumeConfirmation = Pick(p1.RequireVolumeConfirmation, p2.RequireVolumeConfirmation, random)
        };

        Func<MaStrategySettings, bool> validate = settings =>
            settings.FastMaPeriod < settings.SlowMaPeriod;

        // Create generic optimizer
        var optimizer = new GeneticOptimizer<MaStrategySettings>(
            createRandom,
            mutate,
            crossover,
            validate,
            new GeneticOptimizerSettings { PopulationSize = 50, Generations = 30 }
        );

        // Create fitness evaluator
        Func<MaStrategySettings, decimal> evaluateFitness = settings =>
        {
            if (!validate(settings)) return -1000m;

            try
            {
                var strategy = new MaStrategy(settings);
                var engine = new BacktestEngine(strategy, riskSettings, backtestSettings);
                var result = engine.Run(candles, symbol);

                if (result.Metrics.TotalTrades < 20) return -100m;

                return result.Metrics.SharpeRatio;
            }
            catch
            {
                return -1000m;
            }
        };

        // Run optimization
        var result = optimizer.Optimize(evaluateFitness);

        Console.WriteLine($"Best MA settings: Fast={result.BestSettings.FastMaPeriod}, Slow={result.BestSettings.SlowMaPeriod}");
        Console.WriteLine($"Fitness: {result.BestFitness:F2}");
    }

    /// <summary>
    /// Example 2b: Optimize MA strategy using helper optimizer
    /// </summary>
    public static void OptimizeMaStrategyWithHelper(
        List<Candle> candles,
        string symbol,
        RiskSettings riskSettings,
        BacktestSettings backtestSettings)
    {
        var maOptimizer = new MaStrategyOptimizer(
            config: new MaOptimizerConfig(),
            riskSettings: riskSettings,
            backtestSettings: backtestSettings,
            fitnessFunction: FitnessFunction.RiskAdjusted,
            minTrades: 20);

        var result = maOptimizer.Optimize(
            candles,
            symbol,
            settings: new GeneticOptimizerSettings { PopulationSize = 80, Generations = 40 },
            progress: new Progress<GeneticProgress<MaStrategySettings>>(p =>
            {
                Console.WriteLine($"Gen {p.CurrentGeneration}: Best={p.BestFitness:F2}");
            })
        );

        Console.WriteLine($"Best MA settings: Fast={result.BestSettings.FastMaPeriod}, Slow={result.BestSettings.SlowMaPeriod}");
        Console.WriteLine($"Fitness: {result.BestFitness:F2}");
    }

    /// <summary>
    /// Example 3: Optimize RSI strategy
    /// </summary>
    public static GeneticOptimizer<RsiStrategySettings> CreateRsiOptimizer()
    {
        var random = new Random();

        return new GeneticOptimizer<RsiStrategySettings>(
            createRandom: () => new RsiStrategySettings
            {
                RsiPeriod = random.Next(10, 20),
                OversoldLevel = 20m + (decimal)random.NextDouble() * 15m,
                OverboughtLevel = 65m + (decimal)random.NextDouble() * 15m,
                AtrStopMultiplier = 1.0m + (decimal)random.NextDouble() * 2.5m,
                TakeProfitMultiplier = 1.5m + (decimal)random.NextDouble() * 1.5m,
                UseTrendFilter = random.NextDouble() > 0.5,
                ExitOnNeutral = random.NextDouble() > 0.5
            },
            mutate: settings =>
            {
                var param = random.Next(7);
                return param switch
                {
                    0 => settings with { RsiPeriod = Mutate(settings.RsiPeriod, 10, 20, random) },
                    1 => settings with { OversoldLevel = Mutate(settings.OversoldLevel, 20m, 35m, random) },
                    2 => settings with { OverboughtLevel = Mutate(settings.OverboughtLevel, 65m, 80m, random) },
                    3 => settings with { AtrStopMultiplier = Mutate(settings.AtrStopMultiplier, 1.0m, 3.5m, random) },
                    4 => settings with { TakeProfitMultiplier = Mutate(settings.TakeProfitMultiplier, 1.5m, 3.0m, random) },
                    5 => settings with { UseTrendFilter = !settings.UseTrendFilter },
                    _ => settings with { ExitOnNeutral = !settings.ExitOnNeutral }
                };
            },
            crossover: (p1, p2) => new RsiStrategySettings
            {
                RsiPeriod = Pick(p1.RsiPeriod, p2.RsiPeriod, random),
                OversoldLevel = Pick(p1.OversoldLevel, p2.OversoldLevel, random),
                OverboughtLevel = Pick(p1.OverboughtLevel, p2.OverboughtLevel, random),
                AtrStopMultiplier = Pick(p1.AtrStopMultiplier, p2.AtrStopMultiplier, random),
                TakeProfitMultiplier = Pick(p1.TakeProfitMultiplier, p2.TakeProfitMultiplier, random),
                UseTrendFilter = Pick(p1.UseTrendFilter, p2.UseTrendFilter, random),
                ExitOnNeutral = Pick(p1.ExitOnNeutral, p2.ExitOnNeutral, random)
            },
            validate: settings =>
                settings.OversoldLevel < 50 && settings.OverboughtLevel > 50,
            settings: new GeneticOptimizerSettings { PopulationSize = 80, Generations = 40 }
        );
    }

    /// <summary>
    /// Example 3b: Optimize RSI strategy using helper optimizer
    /// </summary>
    public static void OptimizeRsiStrategyWithHelper(
        List<Candle> candles,
        string symbol,
        RiskSettings riskSettings,
        BacktestSettings backtestSettings)
    {
        var rsiOptimizer = new RsiStrategyOptimizer(
            config: new RsiOptimizerConfig(),
            riskSettings: riskSettings,
            backtestSettings: backtestSettings,
            fitnessFunction: FitnessFunction.RiskAdjusted,
            minTrades: 20);

        var result = rsiOptimizer.Optimize(
            candles,
            symbol,
            settings: new GeneticOptimizerSettings { PopulationSize = 80, Generations = 40 },
            progress: new Progress<GeneticProgress<RsiStrategySettings>>(p =>
            {
                Console.WriteLine($"Gen {p.CurrentGeneration}: Best={p.BestFitness:F2}");
            })
        );

        Console.WriteLine($"Best RSI settings: Period={result.BestSettings.RsiPeriod}");
        Console.WriteLine($"Fitness: {result.BestFitness:F2}");
    }

    // Helper methods
    private static int Mutate(int value, int min, int max, Random random)
    {
        var delta = (max - min) / 4;
        var newValue = value + random.Next(-delta, delta + 1);
        return Math.Clamp(newValue, min, max);
    }

    private static decimal Mutate(decimal value, decimal min, decimal max, Random random)
    {
        var delta = (max - min) / 4;
        var newValue = value + (decimal)(random.NextDouble() * 2 - 1) * delta;
        return Math.Clamp(newValue, min, max);
    }

    private static T Pick<T>(T a, T b, Random random) => random.NextDouble() > 0.5 ? a : b;
}
