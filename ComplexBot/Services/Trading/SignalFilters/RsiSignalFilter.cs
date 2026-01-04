using ComplexBot.Models;

namespace ComplexBot.Services.Trading.SignalFilters;

/// <summary>
/// Filters signals based on RSI overbought/oversold conditions.
/// Example: ADX on 4h generates Buy signal, but RSI on 1h shows overbought (>70) → reject.
/// </summary>
public class RsiSignalFilter : ISignalFilter
{
    private readonly decimal _overboughtThreshold;
    private readonly decimal _oversoldThreshold;
    private readonly FilterMode _mode;

    public string Name => "RSI Filter";
    public FilterMode Mode => _mode;

    /// <summary>
    /// Creates an RSI-based signal filter.
    /// </summary>
    /// <param name="overboughtThreshold">RSI level considered overbought (default 70)</param>
    /// <param name="oversoldThreshold">RSI level considered oversold (default 30)</param>
    /// <param name="mode">How to apply the filter (Confirm/Veto/Score)</param>
    public RsiSignalFilter(
        decimal overboughtThreshold = 70m,
        decimal oversoldThreshold = 30m,
        FilterMode mode = FilterMode.Veto)
    {
        _overboughtThreshold = overboughtThreshold;
        _oversoldThreshold = oversoldThreshold;
        _mode = mode;
    }

    public FilterResult Evaluate(TradeSignal signal, StrategyState filterState)
    {
        // If no RSI value available, can't evaluate
        if (!filterState.IndicatorValue.HasValue)
        {
            return new FilterResult(
                Approved: _mode == FilterMode.Veto, // Veto mode: approve if can't determine, Confirm mode: reject
                Reason: "No RSI value available",
                ConfidenceAdjustment: _mode == FilterMode.Score ? 0.5m : null
            );
        }

        var rsi = filterState.IndicatorValue.Value;

        switch (signal.Type)
        {
            case SignalType.Buy:
                return EvaluateBuySignal(rsi);

            case SignalType.Sell:
                return EvaluateSellSignal(rsi);

            case SignalType.Exit:
            case SignalType.PartialExit:
                // Don't block exit signals
                return new FilterResult(true, "Exit signals not filtered", ConfidenceAdjustment: 1.0m);

            default:
                return new FilterResult(true, "Unknown signal type", ConfidenceAdjustment: 1.0m);
        }
    }

    private FilterResult EvaluateBuySignal(decimal rsi)
    {
        // Buying into overbought is risky
        if (rsi >= _overboughtThreshold)
        {
            return new FilterResult(
                Approved: false,
                Reason: $"RSI overbought ({rsi:F1} >= {_overboughtThreshold})",
                ConfidenceAdjustment: 0.2m // If Score mode, reduce position size significantly
            );
        }

        // Buying in oversold is ideal
        if (rsi <= _oversoldThreshold)
        {
            return new FilterResult(
                Approved: true,
                Reason: $"RSI oversold ({rsi:F1} <= {_oversoldThreshold}) - strong buy confirmation",
                ConfidenceAdjustment: 1.2m // If Score mode, increase position size
            );
        }

        // Neutral zone
        var distanceFromOverbought = _overboughtThreshold - rsi;
        var neutralRange = _overboughtThreshold - _oversoldThreshold;
        var confidence = distanceFromOverbought / neutralRange; // 0.0 (overbought) to 1.0 (oversold)

        return new FilterResult(
            Approved: true,
            Reason: $"RSI neutral ({rsi:F1})",
            ConfidenceAdjustment: 0.5m + (confidence * 0.5m) // 0.5 to 1.0 scaling
        );
    }

    private FilterResult EvaluateSellSignal(decimal rsi)
    {
        // Selling into oversold is risky (might reverse soon)
        if (rsi <= _oversoldThreshold)
        {
            return new FilterResult(
                Approved: false,
                Reason: $"RSI oversold ({rsi:F1} <= {_oversoldThreshold})",
                ConfidenceAdjustment: 0.2m
            );
        }

        // Selling in overbought is ideal
        if (rsi >= _overboughtThreshold)
        {
            return new FilterResult(
                Approved: true,
                Reason: $"RSI overbought ({rsi:F1} >= {_overboughtThreshold}) - strong sell confirmation",
                ConfidenceAdjustment: 1.2m
            );
        }

        // Neutral zone
        var distanceFromOversold = rsi - _oversoldThreshold;
        var neutralRange = _overboughtThreshold - _oversoldThreshold;
        var confidence = distanceFromOversold / neutralRange; // 0.0 (oversold) to 1.0 (overbought)

        return new FilterResult(
            Approved: true,
            Reason: $"RSI neutral ({rsi:F1})",
            ConfidenceAdjustment: 0.5m + (confidence * 0.5m)
        );
    }
}
