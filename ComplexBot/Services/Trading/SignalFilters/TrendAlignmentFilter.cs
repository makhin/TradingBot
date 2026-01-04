using ComplexBot.Models;

namespace ComplexBot.Services.Trading.SignalFilters;

/// <summary>
/// Filters signals based on trend alignment between primary and filter strategies.
/// Example: Primary on 4h says Buy (uptrend), but filter on 1h shows downtrend → reject or reduce.
/// Prevents counter-trend entries on lower timeframes.
/// </summary>
public class TrendAlignmentFilter : ISignalFilter
{
    private readonly FilterMode _mode;
    private readonly bool _requireStrictAlignment;

    public string Name => "Trend Alignment Filter";
    public FilterMode Mode => _mode;

    /// <summary>
    /// Creates a trend alignment filter.
    /// </summary>
    /// <param name="mode">How to apply the filter (Confirm/Veto/Score)</param>
    /// <param name="requireStrictAlignment">If true, rejects misaligned trends. If false, only reduces confidence.</param>
    public TrendAlignmentFilter(
        FilterMode mode = FilterMode.Confirm,
        bool requireStrictAlignment = true)
    {
        _mode = mode;
        _requireStrictAlignment = requireStrictAlignment;
    }

    public FilterResult Evaluate(TradeSignal signal, StrategyState filterState)
    {
        // Exit signals are not filtered
        if (signal.Type == SignalType.Exit || signal.Type == SignalType.PartialExit)
        {
            return new FilterResult(true, "Exit signals not filtered", ConfidenceAdjustment: 1.0m);
        }

        // If filter has no trend information, can't evaluate
        if (!filterState.IsTrending)
        {
            return new FilterResult(
                Approved: _mode == FilterMode.Veto, // Veto: approve if uncertain, Confirm: reject
                Reason: "Filter shows no clear trend",
                ConfidenceAdjustment: 0.5m
            );
        }

        // Check alignment based on signal type
        bool isAligned = signal.Type switch
        {
            SignalType.Buy => CheckBuyAlignment(filterState),
            SignalType.Sell => CheckSellAlignment(filterState),
            _ => true
        };

        if (isAligned)
        {
            return new FilterResult(
                Approved: true,
                Reason: GetAlignmentReason(signal.Type, filterState, true),
                ConfidenceAdjustment: 1.2m // Boost confidence for aligned trends
            );
        }

        // Trends are misaligned
        if (_requireStrictAlignment)
        {
            return new FilterResult(
                Approved: false,
                Reason: GetAlignmentReason(signal.Type, filterState, false),
                ConfidenceAdjustment: 0.2m // Significantly reduce position if using Score mode
            );
        }

        // Allow but reduce confidence
        return new FilterResult(
            Approved: true,
            Reason: GetAlignmentReason(signal.Type, filterState, false) + " (allowed with reduced confidence)",
            ConfidenceAdjustment: 0.5m
        );
    }

    /// <summary>
    /// Checks if filter state supports a Buy signal.
    /// Filter should show bullish conditions (oversold, uptrend, or bullish last signal).
    /// </summary>
    private bool CheckBuyAlignment(StrategyState filterState)
    {
        // Check multiple bullish indicators
        var bullishSignals = 0;
        var bearishSignals = 0;

        // Recent signal alignment
        if (filterState.LastSignal == SignalType.Buy)
            bullishSignals++;
        else if (filterState.LastSignal == SignalType.Sell)
            bearishSignals++;

        // Overbought/oversold alignment
        if (filterState.IsOversold)
            bullishSignals++;
        if (filterState.IsOverbought)
            bearishSignals++;

        // Custom trend indicators (e.g., EMA direction)
        if (filterState.CustomValues.TryGetValue("EmaTrend", out var emaTrend))
        {
            if (emaTrend > 0)
                bullishSignals++;
            else if (emaTrend < 0)
                bearishSignals++;
        }

        // Trend direction
        if (filterState.IsTrending)
        {
            // Assume IsTrending with bullish indicators = uptrend
            // This is a heuristic; ideally we'd have explicit TrendDirection
            if (filterState.IsOversold || filterState.LastSignal == SignalType.Buy)
                bullishSignals++;
            else if (filterState.IsOverbought || filterState.LastSignal == SignalType.Sell)
                bearishSignals++;
        }

        return bullishSignals > bearishSignals;
    }

    /// <summary>
    /// Checks if filter state supports a Sell signal.
    /// Filter should show bearish conditions (overbought, downtrend, or bearish last signal).
    /// </summary>
    private bool CheckSellAlignment(StrategyState filterState)
    {
        var bullishSignals = 0;
        var bearishSignals = 0;

        if (filterState.LastSignal == SignalType.Sell)
            bearishSignals++;
        else if (filterState.LastSignal == SignalType.Buy)
            bullishSignals++;

        if (filterState.IsOverbought)
            bearishSignals++;
        if (filterState.IsOversold)
            bullishSignals++;

        if (filterState.CustomValues.TryGetValue("EmaTrend", out var emaTrend))
        {
            if (emaTrend < 0)
                bearishSignals++;
            else if (emaTrend > 0)
                bullishSignals++;
        }

        if (filterState.IsTrending)
        {
            if (filterState.IsOverbought || filterState.LastSignal == SignalType.Sell)
                bearishSignals++;
            else if (filterState.IsOversold || filterState.LastSignal == SignalType.Buy)
                bullishSignals++;
        }

        return bearishSignals > bullishSignals;
    }

    private string GetAlignmentReason(SignalType signalType, StrategyState filterState, bool aligned)
    {
        var direction = signalType == SignalType.Buy ? "bullish" : "bearish";
        var filterDirection = DetermineFilterDirection(filterState);

        if (aligned)
        {
            return $"Trend aligned: Primary {direction}, Filter {filterDirection}";
        }

        return $"Trend misaligned: Primary {direction}, Filter {filterDirection}";
    }

    private string DetermineFilterDirection(StrategyState filterState)
    {
        if (filterState.IsOverbought)
            return "bearish (overbought)";
        if (filterState.IsOversold)
            return "bullish (oversold)";
        if (filterState.LastSignal == SignalType.Buy)
            return "bullish";
        if (filterState.LastSignal == SignalType.Sell)
            return "bearish";
        return "neutral";
    }
}
