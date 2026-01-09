using TradingBot.Core.Models;

namespace ComplexBot.Services.Backtesting;

public class PerformanceFitnessCalculator
{
    public PerformanceFitnessCalculator(PerformanceFitnessPolicy policy)
    {
        Policy = policy;
    }

    public PerformanceFitnessPolicy Policy { get; }

    public decimal InvalidSettingsPenalty => Policy.InvalidSettingsPenalty;

    public bool MeetsPolicy(PerformanceMetrics metrics) =>
        metrics.TotalTrades >= Policy.MinTrades && metrics.MaxDrawdownPercent <= Policy.MaxDrawdownPercent;

    public decimal CalculateFitness(FitnessFunction function, PerformanceMetrics metrics)
    {
        var policyPenalty = GetPolicyPenalty(metrics);
        if (policyPenalty.HasValue)
        {
            return policyPenalty.Value;
        }

        return function switch
        {
            FitnessFunction.Sharpe => metrics.SharpeRatio,
            FitnessFunction.Sortino => metrics.SortinoRatio,
            FitnessFunction.ProfitFactor => metrics.ProfitFactor,
            FitnessFunction.Return => metrics.TotalReturn,
            FitnessFunction.RiskAdjusted => CalculateRiskAdjustedFitness(metrics),
            FitnessFunction.Combined => CalculateCombinedFitness(metrics),
            _ => metrics.SharpeRatio
        };
    }

    public decimal CalculateScore(OptimizationTarget target, PerformanceMetrics metrics)
    {
        var policyPenalty = GetPolicyPenalty(metrics);
        if (policyPenalty.HasValue)
        {
            return policyPenalty.Value;
        }

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

    private decimal? GetPolicyPenalty(PerformanceMetrics metrics)
    {
        if (metrics.TotalTrades < Policy.MinTrades)
        {
            return Policy.InsufficientTradesPenalty;
        }

        if (metrics.MaxDrawdownPercent > Policy.MaxDrawdownPercent)
        {
            return Policy.MaxDrawdownPenalty;
        }

        return null;
    }

    private decimal CalculateRiskAdjustedFitness(PerformanceMetrics metrics)
    {
        var drawdownPenalty = Math.Max(0, metrics.MaxDrawdownPercent - Policy.DrawdownPenaltyThresholdPercent)
            * Policy.DrawdownPenaltyFactor;
        return metrics.SharpeRatio - drawdownPenalty;
    }

    private static decimal CalculateCombinedFitness(PerformanceMetrics metrics)
    {
        var sharpeComponent = Math.Max(0, metrics.SharpeRatio);
        var pfComponent = 1 + Math.Min(3, metrics.ProfitFactor) / 10;
        var ddComponent = 1 - Math.Min(1, metrics.MaxDrawdownPercent / 100);

        return sharpeComponent * pfComponent * ddComponent;
    }
}
