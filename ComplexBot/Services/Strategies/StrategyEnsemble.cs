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
    /// Create ensemble with default strategies using weights from settings.
    ///
    /// Philosophy: Combines TWO trading approaches for balance:
    /// 1. Trend Following (ADX, MA Crossover) - trades WITH the trend, catches strong moves
    /// 2. Mean Reversion (RSI) - trades AGAINST extremes, catches pullbacks
    ///
    /// This creates diversification:
    /// - In strong trend: ADX/MA dominate, RSI stays quiet
    /// - In ranging market: RSI may signal, ADX/MA filter
    /// - When ALL agree (≥60%): maximum confidence in the signal
    /// </summary>
    public static StrategyEnsemble CreateDefault(EnsembleSettings? settings = null)
    {
        var effectiveSettings = settings ?? new EnsembleSettings();
        var ensemble = new StrategyEnsemble(effectiveSettings);

        // Create strategies: mix of trend-following and mean-reversion
        var adxStrategy = new AdxTrendStrategy();   // Trend following
        var maStrategy = new MaStrategy();          // Trend following
        var rsiStrategy = new RsiStrategy();        // Mean reversion (counter-trend)

        // Get weights from settings, fallback to hardcoded defaults if not found
        var adxWeight = effectiveSettings.StrategyWeights.TryGetValue(StrategyKind.AdxTrendFollowing, out var adxW)
            ? adxW : 0.5m;
        var maWeight = effectiveSettings.StrategyWeights.TryGetValue(StrategyKind.MaCrossover, out var maW)
            ? maW : 0.25m;
        var rsiWeight = effectiveSettings.StrategyWeights.TryGetValue(StrategyKind.RsiMeanReversion, out var rsiW)
            ? rsiW : 0.25m;

        ensemble.AddStrategy(adxStrategy, adxWeight);
        ensemble.AddStrategy(maStrategy, maWeight);
        ensemble.AddStrategy(rsiStrategy, rsiWeight);

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

            return TradeSignal.Create(
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

            return TradeSignal.Create(
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

            return TradeSignal.Create(
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

            return TradeSignal.Create(
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
