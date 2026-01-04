using ComplexBot.Models;

namespace ComplexBot.Services.Trading.SignalFilters;

/// <summary>
/// Filters signals based on ADX trend strength.
/// Example: Primary strategy on 4h generates signal, but ADX on 1h shows weak trend → reduce position or reject.
/// </summary>
public class AdxSignalFilter : ISignalFilter
{
    private readonly decimal _minTrendStrength;
    private readonly decimal _strongTrendThreshold;
    private readonly FilterMode _mode;

    public string Name => "ADX Trend Strength Filter";
    public FilterMode Mode => _mode;

    /// <summary>
    /// Creates an ADX-based trend strength filter.
    /// </summary>
    /// <param name="minTrendStrength">Minimum ADX to approve signal (default 20)</param>
    /// <param name="strongTrendThreshold">ADX level indicating strong trend (default 30)</param>
    /// <param name="mode">How to apply the filter (Confirm/Veto/Score)</param>
    public AdxSignalFilter(
        decimal minTrendStrength = 20m,
        decimal strongTrendThreshold = 30m,
        FilterMode mode = FilterMode.Score)
    {
        _minTrendStrength = minTrendStrength;
        _strongTrendThreshold = strongTrendThreshold;
        _mode = mode;
    }

    public FilterResult Evaluate(TradeSignal signal, StrategyState filterState)
    {
        // If no ADX value available, can't evaluate
        if (!filterState.IndicatorValue.HasValue)
        {
            return new FilterResult(
                Approved: _mode == FilterMode.Veto,
                Reason: "No ADX value available",
                ConfidenceAdjustment: _mode == FilterMode.Score ? 0.5m : null
            );
        }

        var adx = filterState.IndicatorValue.Value;

        // Exit signals should not be blocked by weak trends
        if (signal.Type == SignalType.Exit || signal.Type == SignalType.PartialExit)
        {
            return new FilterResult(true, "Exit signals not filtered", ConfidenceAdjustment: 1.0m);
        }

        // Check trend strength
        if (adx < _minTrendStrength)
        {
            return new FilterResult(
                Approved: false,
                Reason: $"Trend too weak (ADX {adx:F1} < {_minTrendStrength})",
                ConfidenceAdjustment: CalculateConfidence(adx)
            );
        }

        // Strong trend
        if (adx >= _strongTrendThreshold)
        {
            return new FilterResult(
                Approved: true,
                Reason: $"Strong trend confirmed (ADX {adx:F1} >= {_strongTrendThreshold})",
                ConfidenceAdjustment: 1.2m // Increase position size for strong trends
            );
        }

        // Moderate trend
        return new FilterResult(
            Approved: true,
            Reason: $"Moderate trend (ADX {adx:F1})",
            ConfidenceAdjustment: CalculateConfidence(adx)
        );
    }

    /// <summary>
    /// Calculates confidence based on ADX value.
    /// Returns 0.2 for very weak trends, 1.2 for very strong trends.
    /// </summary>
    private decimal CalculateConfidence(decimal adx)
    {
        if (adx < _minTrendStrength)
        {
            // Very weak trend: 0.2 - 0.5 confidence
            var weakRatio = adx / _minTrendStrength;
            return 0.2m + (weakRatio * 0.3m);
        }

        if (adx >= _strongTrendThreshold)
        {
            // Strong trend: 1.0 - 1.2 confidence
            // Cap at ADX 50 for scaling
            var excessStrength = Math.Min(adx - _strongTrendThreshold, 20m);
            return 1.0m + (excessStrength / 20m * 0.2m);
        }

        // Moderate trend: 0.5 - 1.0 confidence
        // Linear interpolation between min and strong threshold
        var range = _strongTrendThreshold - _minTrendStrength;
        var position = adx - _minTrendStrength;
        var ratio = position / range;
        return 0.5m + (ratio * 0.5m);
    }
}
