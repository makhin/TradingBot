using ComplexBot.Services.Strategies;

namespace ComplexBot.Configuration.Strategy;

public class StrategyConfigSettings
{
    // ADX settings
    public int AdxPeriod { get; set; } = 14;
    public decimal AdxThreshold { get; set; } = 25m;
    public decimal AdxExitThreshold { get; set; } = 18m;
    public bool RequireFreshTrend { get; set; } = false;
    public bool RequireAdxRising { get; set; } = false;
    public int AdxSlopeLookback { get; set; } = 5;
    public int AdxFallingExitBars { get; set; } = 0;
    public int MaxBarsInTrade { get; set; } = 0;

    // EMA settings
    public int FastEmaPeriod { get; set; } = 20;
    public int SlowEmaPeriod { get; set; } = 50;

    // ATR settings
    public int AtrPeriod { get; set; } = 14;
    public decimal AtrStopMultiplier { get; set; } = 2.5m;
    public decimal TakeProfitMultiplier { get; set; } = 1.5m;
    public decimal MinAtrPercent { get; set; } = 0m;
    public decimal MaxAtrPercent { get; set; } = 100m;

    // Volume settings
    public int VolumePeriod { get; set; } = 20;
    public decimal VolumeThreshold { get; set; } = 1.5m;
    public bool RequireVolumeConfirmation { get; set; } = true;

    // OBV settings
    public int ObvPeriod { get; set; } = 20;
    public bool RequireObvConfirmation { get; set; } = true;

    // Partial exit settings
    public decimal PartialExitRMultiple { get; set; } = 1m;
    public decimal PartialExitFraction { get; set; } = 0.5m;

    public StrategySettings ToStrategySettings() => new()
    {
        AdxPeriod = AdxPeriod,
        AdxThreshold = AdxThreshold,
        AdxExitThreshold = AdxExitThreshold,
        RequireFreshTrend = RequireFreshTrend,
        RequireAdxRising = RequireAdxRising,
        AdxSlopeLookback = AdxSlopeLookback,
        AdxFallingExitBars = AdxFallingExitBars,
        MaxBarsInTrade = MaxBarsInTrade,
        FastEmaPeriod = FastEmaPeriod,
        SlowEmaPeriod = SlowEmaPeriod,
        AtrPeriod = AtrPeriod,
        AtrStopMultiplier = AtrStopMultiplier,
        TakeProfitMultiplier = TakeProfitMultiplier,
        MinAtrPercent = MinAtrPercent,
        MaxAtrPercent = MaxAtrPercent,
        VolumePeriod = VolumePeriod,
        VolumeThreshold = VolumeThreshold,
        RequireVolumeConfirmation = RequireVolumeConfirmation,
        ObvPeriod = ObvPeriod,
        RequireObvConfirmation = RequireObvConfirmation,
        PartialExitRMultiple = PartialExitRMultiple,
        PartialExitFraction = PartialExitFraction
    };

    public static StrategyConfigSettings FromSettings(StrategySettings settings) => new()
    {
        AdxPeriod = settings.AdxPeriod,
        AdxThreshold = settings.AdxThreshold,
        AdxExitThreshold = settings.AdxExitThreshold,
        RequireFreshTrend = settings.RequireFreshTrend,
        RequireAdxRising = settings.RequireAdxRising,
        AdxSlopeLookback = settings.AdxSlopeLookback,
        AdxFallingExitBars = settings.AdxFallingExitBars,
        MaxBarsInTrade = settings.MaxBarsInTrade,
        FastEmaPeriod = settings.FastEmaPeriod,
        SlowEmaPeriod = settings.SlowEmaPeriod,
        AtrPeriod = settings.AtrPeriod,
        AtrStopMultiplier = settings.AtrStopMultiplier,
        TakeProfitMultiplier = settings.TakeProfitMultiplier,
        MinAtrPercent = settings.MinAtrPercent,
        MaxAtrPercent = settings.MaxAtrPercent,
        VolumePeriod = settings.VolumePeriod,
        VolumeThreshold = settings.VolumeThreshold,
        RequireVolumeConfirmation = settings.RequireVolumeConfirmation,
        ObvPeriod = settings.ObvPeriod,
        RequireObvConfirmation = settings.RequireObvConfirmation,
        PartialExitRMultiple = settings.PartialExitRMultiple,
        PartialExitFraction = settings.PartialExitFraction
    };
}
