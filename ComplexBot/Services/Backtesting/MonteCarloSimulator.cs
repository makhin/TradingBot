using TradingBot.Core.Models;
using ComplexBot.Models;

namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Monte Carlo Simulation for strategy robustness validation
/// Tests how strategy performance varies with trade order randomization
/// </summary>
public class MonteCarloSimulator
{
    private readonly Random _random;
    private readonly int _simulations;

    public MonteCarloSimulator(int simulations = 1000, int? seed = null)
    {
        _simulations = simulations;
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public MonteCarloResult Simulate(BacktestResult backtestResult)
    {
        var originalTrades = backtestResult.Trades
            .Where(t => t.PnL.HasValue)
            .ToList();

        if (originalTrades.Count < 10)
        {
            return new MonteCarloResult(
                backtestResult.Metrics.TotalReturn,
                backtestResult.Metrics.TotalReturn,
                0, 0, 0, 0, 0, 0,
                new List<decimal>(),
                new List<decimal>()
            );
        }

        var simulatedReturns = new List<decimal>();
        var simulatedDrawdowns = new List<decimal>();

        for (int i = 0; i < _simulations; i++)
        {
            var shuffledTrades = ShuffleTrades(originalTrades);
            var (totalReturn, maxDrawdown) = SimulateEquityCurve(
                shuffledTrades, 
                backtestResult.InitialCapital
            );
            
            simulatedReturns.Add(totalReturn);
            simulatedDrawdowns.Add(maxDrawdown);
        }

        simulatedReturns.Sort();
        simulatedDrawdowns.Sort();

        // Calculate percentiles
        decimal median = GetPercentile(simulatedReturns, 50);
        decimal p5Return = GetPercentile(simulatedReturns, 5);
        decimal p95Return = GetPercentile(simulatedReturns, 95);
        decimal p5Drawdown = GetPercentile(simulatedDrawdowns, 5);
        decimal p95Drawdown = GetPercentile(simulatedDrawdowns, 95);
        decimal avgDrawdown = simulatedDrawdowns.Average();

        return new MonteCarloResult(
            backtestResult.Metrics.TotalReturn,
            median,
            p5Return,
            p95Return,
            avgDrawdown,
            p5Drawdown,
            p95Drawdown,
            CalculateRuinProbability(simulatedReturns),
            simulatedReturns,
            simulatedDrawdowns
        );
    }

    private List<Trade> ShuffleTrades(List<Trade> trades)
    {
        var shuffled = trades.ToList();
        int n = shuffled.Count;
        
        while (n > 1)
        {
            n--;
            int k = _random.Next(n + 1);
            (shuffled[k], shuffled[n]) = (shuffled[n], shuffled[k]);
        }
        
        return shuffled;
    }

    private (decimal totalReturn, decimal maxDrawdown) SimulateEquityCurve(
        List<Trade> trades, 
        decimal initialCapital)
    {
        decimal equity = initialCapital;
        decimal peak = initialCapital;
        decimal maxDrawdown = 0;

        foreach (var trade in trades)
        {
            equity += trade.PnL ?? 0;
            
            if (equity > peak)
                peak = equity;
            
            decimal drawdown = (peak - equity) / peak * 100;
            if (drawdown > maxDrawdown)
                maxDrawdown = drawdown;
        }

        decimal totalReturn = (equity - initialCapital) / initialCapital * 100;
        return (totalReturn, maxDrawdown);
    }

    private decimal GetPercentile(List<decimal> sorted, int percentile)
    {
        int index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        index = Math.Max(0, Math.Min(index, sorted.Count - 1));
        return sorted[index];
    }

    private decimal CalculateRuinProbability(List<decimal> returns)
    {
        // Probability of losing 50% or more
        int ruinCount = returns.Count(r => r <= -50);
        return (decimal)ruinCount / returns.Count * 100;
    }
}
