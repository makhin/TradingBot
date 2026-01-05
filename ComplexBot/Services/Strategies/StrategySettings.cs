namespace ComplexBot.Services.Strategies;

public record StrategySettings
{
    // ADX settings
    public int AdxPeriod { get; init; } = 14;
    public decimal AdxThreshold { get; init; } = 25m;  // Entry: ADX > 25
    public decimal AdxExitThreshold { get; init; } = 18m;  // Exit: ADX < 18
    public bool RequireFreshTrend { get; init; } = false;
    public bool RequireAdxRising { get; init; } = false;
    public int AdxSlopeLookback { get; init; } = 5;
    public int AdxFallingExitBars { get; init; } = 0;
    public int MaxBarsInTrade { get; init; } = 0;

    // EMA settings (research: 20/50 optimal for medium-term)
    public int FastEmaPeriod { get; init; } = 20;
    public int SlowEmaPeriod { get; init; } = 50;

    // ATR settings
    public int AtrPeriod { get; init; } = 14;
    public decimal AtrStopMultiplier { get; init; } = 2.5m;  // 2.5x ATR for medium-term
    public decimal TakeProfitMultiplier { get; init; } = 1.5m;  // 1.5:1 reward:risk
    public decimal MinAtrPercent { get; init; } = 0m;
    public decimal MaxAtrPercent { get; init; } = 100m;

    // Volume confirmation (research: 1.5-2x average volume confirms breakouts)
    public int VolumePeriod { get; init; } = 20;
    public decimal VolumeThreshold { get; init; } = 1.5m;  // 1.5x average volume
    public bool RequireVolumeConfirmation { get; init; } = true;

    // OBV settings
    public int ObvPeriod { get; init; } = 20;
    public bool RequireObvConfirmation { get; init; } = true;

    // Partial exit settings
    public decimal PartialExitRMultiple { get; init; } = 1m;
    public decimal PartialExitFraction { get; init; } = 0.5m;
}
