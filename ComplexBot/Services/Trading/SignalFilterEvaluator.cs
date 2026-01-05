using ComplexBot.Models;
using ComplexBot.Services.Strategies;

namespace ComplexBot.Services.Trading;

public static class SignalFilterEvaluator
{
    public static FilterResult Evaluate(
        TradeSignal signal,
        IReadOnlyList<(ISignalFilter Filter, StrategyState State)> filters)
    {
        if (filters.Count == 0)
        {
            return new FilterResult(true, "No filters", ConfidenceAdjustment: 1.0m);
        }

        var filterResults = new List<(ISignalFilter Filter, FilterResult Result)>();

        foreach (var (filter, state) in filters)
        {
            FilterResult result;
            if (!IsFilterStateReady(state))
            {
                result = new FilterResult(
                    Approved: true,
                    Reason: "Filter state not ready; skipping filter evaluation",
                    ConfidenceAdjustment: 1.0m);
            }
            else
            {
                result = filter.Evaluate(signal, state);
            }

            filterResults.Add((filter, result));
        }

        return CombineFilterResults(filterResults);
    }

    public static FilterResult CombineFilterResults(
        List<(ISignalFilter Filter, FilterResult Result)> filterResults)
    {
        var confirmFilters = filterResults.Where(f => f.Filter.Mode == FilterMode.Confirm).ToList();
        var vetoFilters = filterResults.Where(f => f.Filter.Mode == FilterMode.Veto).ToList();
        var scoreFilters = filterResults.Where(f => f.Filter.Mode == FilterMode.Score).ToList();

        if (confirmFilters.Any())
        {
            var rejected = confirmFilters.FirstOrDefault(f => !f.Result.Approved);
            if (rejected.Filter != null)
            {
                return new FilterResult(
                    Approved: false,
                    Reason: $"Confirm filter '{rejected.Filter.Name}' rejected: {rejected.Result.Reason}",
                    ConfidenceAdjustment: rejected.Result.ConfidenceAdjustment
                );
            }
        }

        if (vetoFilters.Any())
        {
            var rejected = vetoFilters.FirstOrDefault(f => !f.Result.Approved);
            if (rejected.Filter != null)
            {
                return new FilterResult(
                    Approved: false,
                    Reason: $"Veto filter '{rejected.Filter.Name}' rejected: {rejected.Result.Reason}",
                    ConfidenceAdjustment: rejected.Result.ConfidenceAdjustment
                );
            }
        }

        decimal combinedConfidence = 1.0m;
        var scoreReasons = new List<string>();

        foreach (var (filter, result) in scoreFilters)
        {
            if (result.ConfidenceAdjustment.HasValue)
            {
                combinedConfidence *= result.ConfidenceAdjustment.Value;
                scoreReasons.Add($"{filter.Name}: {result.ConfidenceAdjustment:F2}x");
            }
        }

        var reason = scoreReasons.Any()
            ? $"Score filters applied: {string.Join(", ", scoreReasons)}"
            : "All filters approved";

        return new FilterResult(
            Approved: true,
            Reason: reason,
            ConfidenceAdjustment: combinedConfidence
        );
    }

    public static TradeSignal ApplyConfidenceAdjustment(TradeSignal original, decimal? confidenceAdjustment)
    {
        if (!confidenceAdjustment.HasValue || confidenceAdjustment.Value == 1.0m)
        {
            return original;
        }

        var adjustedReason = $"{original.Reason} [Confidence: {confidenceAdjustment:F2}x]";

        return new TradeSignal(
            Symbol: original.Symbol,
            Type: original.Type,
            Price: original.Price,
            StopLoss: original.StopLoss,
            TakeProfit: original.TakeProfit,
            Reason: adjustedReason
        );
    }

    public static bool IsFilterStateReady(StrategyState filterState)
    {
        return filterState.IndicatorValue.HasValue
            || filterState.LastSignal.HasValue
            || filterState.IsOverbought
            || filterState.IsOversold
            || filterState.IsTrending
            || filterState.CustomValues.Count > 0;
    }
}
