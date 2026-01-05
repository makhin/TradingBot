namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Full optimization settings including all strategy parameters
/// </summary>
public record FullEnsembleSettings
{
    // === Ensemble Weights ===
    public decimal AdxWeight { get; init; } = 0.5m;
    public decimal MaWeight { get; init; } = 0.25m;
    public decimal RsiWeight { get; init; } = 0.25m;
    public decimal MinimumAgreement { get; init; } = 0.6m;
    public bool UseConfidenceWeighting { get; init; } = true;

    // === ADX Strategy Parameters ===
    public int AdxPeriod { get; init; } = 14;
    public decimal AdxThreshold { get; init; } = 25m;
    public decimal AdxExitThreshold { get; init; } = 18m;
    public int AdxFastEmaPeriod { get; init; } = 20;
    public int AdxSlowEmaPeriod { get; init; } = 50;
    public decimal AdxAtrStopMultiplier { get; init; } = 2.5m;
    public decimal AdxVolumeThreshold { get; init; } = 1.5m;

    // === MA Strategy Parameters ===
    public int MaFastPeriod { get; init; } = 10;
    public int MaSlowPeriod { get; init; } = 30;
    public decimal MaAtrStopMultiplier { get; init; } = 2.0m;
    public decimal MaTakeProfitMultiplier { get; init; } = 2.0m;
    public decimal MaVolumeThreshold { get; init; } = 1.2m;

    // === RSI Strategy Parameters ===
    public int RsiPeriod { get; init; } = 14;
    public decimal RsiOversoldLevel { get; init; } = 30m;
    public decimal RsiOverboughtLevel { get; init; } = 70m;
    public decimal RsiAtrStopMultiplier { get; init; } = 1.5m;
    public decimal RsiTakeProfitMultiplier { get; init; } = 2.0m;
    public bool RsiUseTrendFilter { get; init; } = true;

    public override string ToString()
    {
        return $"""
            === Ensemble Weights ===
            ADX: {AdxWeight:P0}, MA: {MaWeight:P0}, RSI: {RsiWeight:P0}
            Agreement: {MinimumAgreement:P0}, ConfidenceWeighting: {UseConfidenceWeighting}

            === ADX Settings ===
            Period: {AdxPeriod}, Threshold: {AdxThreshold}, Exit: {AdxExitThreshold}
            EMA: {AdxFastEmaPeriod}/{AdxSlowEmaPeriod}, ATR Stop: {AdxAtrStopMultiplier}x, Volume: {AdxVolumeThreshold}x

            === MA Settings ===
            Fast: {MaFastPeriod}, Slow: {MaSlowPeriod}
            ATR Stop: {MaAtrStopMultiplier}x, TP: {MaTakeProfitMultiplier}x, Volume: {MaVolumeThreshold}x

            === RSI Settings ===
            Period: {RsiPeriod}, Oversold: {RsiOversoldLevel}, Overbought: {RsiOverboughtLevel}
            ATR Stop: {RsiAtrStopMultiplier}x, TP: {RsiTakeProfitMultiplier}x, TrendFilter: {RsiUseTrendFilter}
            """;
    }
}
