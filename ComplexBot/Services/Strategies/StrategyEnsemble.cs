using ComplexBot.Models;

namespace ComplexBot.Services.Strategies;

/// <summary>
/// Strategy Ensemble - combines signals from multiple strategies using weighted voting.
/// Only generates a signal when strategies reach consensus above minimum agreement threshold.
/// </summary>
public class StrategyEnsemble : IStrategy, IHasConfidence
{
    public string Name => "Strategy Ensemble";
    public decimal? CurrentStopLoss => null;
    public decimal? CurrentAtr => _strategies
        .Select(s => s.Strategy.CurrentAtr)
        .Where(a => a.HasValue)
        .Select(a => a!.Value)
        .DefaultIfEmpty(0)
        .Average() is var avg && avg > 0 ? avg : null;

    public decimal GetConfidence()
    {
        if (_strategies.Count == 0)
            return 0.5m;

        // Average confidence of all strategies that support confidence
        var confidenceStrategies = _strategies
            .Where(s => s.Strategy is IHasConfidence)
            .ToList();

        if (confidenceStrategies.Count == 0)
            return 0.5m;

        return confidenceStrategies.Average(s => ((IHasConfidence)s.Strategy).GetConfidence());
    }

    private readonly EnsembleSettings _settings;
    private readonly List<StrategyWeight> _strategies;
    private readonly List<StrategyVote> _lastVotes = new();

    public StrategyEnsemble(EnsembleSettings? settings = null)
    {
        _settings = settings ?? new EnsembleSettings();
        _strategies = new List<StrategyWeight>();
    }

    /// <summary>
    /// Create ensemble with default strategies
    /// </summary>
    public static StrategyEnsemble CreateDefault(EnsembleSettings? settings = null)
    {
        var ensemble = new StrategyEnsemble(settings);

        ensemble.AddStrategy(new AdxTrendStrategy(), 0.4m);
        ensemble.AddStrategy(new MaStrategy(), 0.3m);
        ensemble.AddStrategy(new RsiStrategy(), 0.3m);

        return ensemble;
    }

    public void AddStrategy(IStrategy strategy, decimal weight)
    {
        if (weight <= 0 || weight > 1)
            throw new ArgumentException("Weight must be between 0 and 1");

        _strategies.Add(new StrategyWeight(strategy, weight));
    }

    public void RemoveStrategy(string name)
    {
        _strategies.RemoveAll(s => s.Strategy.Name == name);
    }

    public IReadOnlyList<StrategyVote> LastVotes => _lastVotes;

    public TradeSignal? Analyze(Candle candle, decimal? currentPosition, string symbol)
    {
        if (_strategies.Count == 0)
            return null;

        _lastVotes.Clear();

        // Collect votes from all strategies
        foreach (var sw in _strategies)
        {
            var signal = sw.Strategy.Analyze(candle, currentPosition, symbol);
            var confidence = GetStrategyConfidence(sw.Strategy);

            _lastVotes.Add(new StrategyVote
            {
                StrategyName = sw.Strategy.Name,
                Signal = signal?.Type ?? SignalType.None,
                Confidence = confidence,
                Weight = sw.Weight,
                StopLoss = signal?.StopLoss,
                TakeProfit = signal?.TakeProfit,
                Reason = signal?.Reason ?? "No signal",
                PartialExitPercent = signal?.PartialExitPercent,
                MoveStopToBreakeven = signal?.MoveStopToBreakeven ?? false
            });
        }

        // Calculate weighted scores for each signal type
        var totalWeight = _strategies.Sum(s => s.Weight);
        var buyScore = CalculateScore(SignalType.Buy, totalWeight);
        var sellScore = CalculateScore(SignalType.Sell, totalWeight);
        var exitScore = CalculateScore(SignalType.Exit, totalWeight);
        var partialExitScore = CalculateScore(SignalType.PartialExit, totalWeight);

        // Check exit consensus first (if we have a position)
        bool hasPosition = currentPosition.HasValue && currentPosition.Value != 0;
        if (hasPosition && exitScore >= _settings.MinimumAgreement)
        {
            var exitVotes = _lastVotes.Where(v => v.Signal == SignalType.Exit).ToList();
            var avgConfidence = exitVotes.Any() ? exitVotes.Average(v => v.Confidence) : 0m;

            return new TradeSignal(
                symbol,
                SignalType.Exit,
                candle.Close,
                null,
                null,
                $"Ensemble Exit: {exitVotes.Count}/{_strategies.Count} strategies agree ({exitScore:P0} consensus)"
            );
        }

        // Check partial exit consensus (if we have a position)
        if (hasPosition && partialExitScore >= _settings.MinimumAgreement)
        {
            var partialExitVotes = _lastVotes.Where(v => v.Signal == SignalType.PartialExit).ToList();

            // Average partial exit percent from all voting strategies
            var avgPartialExitPercent = partialExitVotes
                .Where(v => v.PartialExitPercent.HasValue)
                .Select(v => v.PartialExitPercent!.Value)
                .DefaultIfEmpty(0.5m)
                .Average();

            // Use consensus if any strategy wants to move to breakeven
            var moveToBreakeven = partialExitVotes.Any(v => v.MoveStopToBreakeven);

            // Get consensus new stop loss for partial exit
            var newStopLoss = partialExitVotes
                .Where(v => v.StopLoss.HasValue)
                .Select(v => v.StopLoss!.Value)
                .DefaultIfEmpty()
                .FirstOrDefault();

            return new TradeSignal(
                symbol,
                SignalType.PartialExit,
                candle.Close,
                newStopLoss,
                null,
                $"Ensemble Partial Exit: {partialExitVotes.Count}/{_strategies.Count} strategies ({partialExitScore:P0} consensus)",
                PartialExitPercent: avgPartialExitPercent,
                MoveStopToBreakeven: moveToBreakeven
            );
        }

        // Check buy consensus
        if (!hasPosition && buyScore >= _settings.MinimumAgreement)
        {
            var buyVotes = _lastVotes.Where(v => v.Signal == SignalType.Buy).ToList();
            var stopLoss = GetConsensusStopLoss(buyVotes, candle.Close, true);
            var takeProfit = GetConsensusTakeProfit(buyVotes, candle.Close, true);

            return new TradeSignal(
                symbol,
                SignalType.Buy,
                candle.Close,
                stopLoss,
                takeProfit,
                $"Ensemble Buy: {buyVotes.Count}/{_strategies.Count} strategies ({buyScore:P0} consensus)"
            );
        }

        // Check sell consensus
        if (!hasPosition && sellScore >= _settings.MinimumAgreement)
        {
            var sellVotes = _lastVotes.Where(v => v.Signal == SignalType.Sell).ToList();
            var stopLoss = GetConsensusStopLoss(sellVotes, candle.Close, false);
            var takeProfit = GetConsensusTakeProfit(sellVotes, candle.Close, false);

            return new TradeSignal(
                symbol,
                SignalType.Sell,
                candle.Close,
                stopLoss,
                takeProfit,
                $"Ensemble Sell: {sellVotes.Count}/{_strategies.Count} strategies ({sellScore:P0} consensus)"
            );
        }

        return null;
    }

    private decimal CalculateScore(SignalType signalType, decimal totalWeight)
    {
        if (totalWeight <= 0)
            return 0;

        var votes = _lastVotes.Where(v => v.Signal == signalType);

        return _settings.UseConfidenceWeighting
            ? votes.Sum(v => v.Weight * v.Confidence) / totalWeight
            : votes.Sum(v => v.Weight) / totalWeight;
    }

    private decimal GetStrategyConfidence(IStrategy strategy)
    {
        return strategy is IHasConfidence hasConfidence
            ? hasConfidence.GetConfidence()
            : 0.5m; // Default confidence for strategies without IHasConfidence
    }

    private static decimal? GetConsensusStopLoss(List<StrategyVote> votes, decimal price, bool isLong)
    {
        var stopsWithValue = votes.Where(v => v.StopLoss.HasValue).ToList();
        if (!stopsWithValue.Any())
            return null;

        // For longs: use the most conservative (highest) stop loss
        // For shorts: use the most conservative (lowest) stop loss
        if (isLong)
            return stopsWithValue.Max(v => v.StopLoss!.Value);
        else
            return stopsWithValue.Min(v => v.StopLoss!.Value);
    }

    private static decimal? GetConsensusTakeProfit(List<StrategyVote> votes, decimal price, bool isLong)
    {
        var tpsWithValue = votes.Where(v => v.TakeProfit.HasValue).ToList();
        if (!tpsWithValue.Any())
            return null;

        // For longs: use the most conservative (lowest) take profit
        // For shorts: use the most conservative (highest) take profit
        if (isLong)
            return tpsWithValue.Min(v => v.TakeProfit!.Value);
        else
            return tpsWithValue.Max(v => v.TakeProfit!.Value);
    }

    public void Reset()
    {
        foreach (var sw in _strategies)
        {
            sw.Strategy.Reset();
        }
        _lastVotes.Clear();
    }

    public EnsembleStats GetStats()
    {
        return new EnsembleStats
        {
            StrategyCount = _strategies.Count,
            Strategies = _strategies.Select(s => new StrategyInfo
            {
                Name = s.Strategy.Name,
                Weight = s.Weight
            }).ToList(),
            LastVotes = _lastVotes.ToList(),
            MinimumAgreement = _settings.MinimumAgreement,
            UseConfidenceWeighting = _settings.UseConfidenceWeighting
        };
    }
}

public record StrategyWeight(IStrategy Strategy, decimal Weight);

public record StrategyVote
{
    public string StrategyName { get; init; } = "";
    public SignalType Signal { get; init; }
    public decimal Confidence { get; init; }
    public decimal Weight { get; init; }
    public decimal? StopLoss { get; init; }
    public decimal? TakeProfit { get; init; }
    public string Reason { get; init; } = "";
    public decimal? PartialExitPercent { get; init; }
    public bool MoveStopToBreakeven { get; init; }
}

public record EnsembleSettings
{
    /// <summary>
    /// Minimum weighted agreement required for signal (0.0-1.0)
    /// Default: 0.6 (60% agreement)
    /// </summary>
    public decimal MinimumAgreement { get; init; } = 0.6m;

    /// <summary>
    /// Whether to weight votes by strategy confidence
    /// If true: score = sum(weight * confidence) / totalWeight
    /// If false: score = sum(weight) / totalWeight
    /// </summary>
    public bool UseConfidenceWeighting { get; init; } = true;

    /// <summary>
    /// Default strategy weights
    /// </summary>
    public Dictionary<string, decimal> StrategyWeights { get; init; } = new()
    {
        ["ADX Trend Following + Volume"] = 0.4m,
        ["MA Crossover"] = 0.3m,
        ["RSI Mean Reversion"] = 0.3m
    };
}

public record EnsembleStats
{
    public int StrategyCount { get; init; }
    public List<StrategyInfo> Strategies { get; init; } = new();
    public List<StrategyVote> LastVotes { get; init; } = new();
    public decimal MinimumAgreement { get; init; }
    public bool UseConfidenceWeighting { get; init; }
}

public record StrategyInfo
{
    public string Name { get; init; } = "";
    public decimal Weight { get; init; }
}
