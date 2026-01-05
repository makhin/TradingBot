using ComplexBot.Services.Strategies;

namespace ComplexBot.Configuration.Strategy;

public class RsiStrategyConfigSettings
{
    public int RsiPeriod { get; set; } = 14;
    public decimal OversoldLevel { get; set; } = 30m;
    public decimal OverboughtLevel { get; set; } = 70m;
    public decimal NeutralZoneLow { get; set; } = 45m;
    public decimal NeutralZoneHigh { get; set; } = 55m;
    public bool ExitOnNeutral { get; set; }
    public int AtrPeriod { get; set; } = 14;
    public decimal AtrStopMultiplier { get; set; } = 1.5m;
    public decimal TakeProfitMultiplier { get; set; } = 2.0m;
    public int TrendFilterPeriod { get; set; } = 50;
    public bool UseTrendFilter { get; set; } = true;
    public int VolumePeriod { get; set; } = 20;
    public decimal VolumeThreshold { get; set; } = 1.0m;
    public bool RequireVolumeConfirmation { get; set; }

    public RsiStrategySettings ToRsiStrategySettings() => new()
    {
        RsiPeriod = RsiPeriod,
        OversoldLevel = OversoldLevel,
        OverboughtLevel = OverboughtLevel,
        NeutralZoneLow = NeutralZoneLow,
        NeutralZoneHigh = NeutralZoneHigh,
        ExitOnNeutral = ExitOnNeutral,
        AtrPeriod = AtrPeriod,
        AtrStopMultiplier = AtrStopMultiplier,
        TakeProfitMultiplier = TakeProfitMultiplier,
        TrendFilterPeriod = TrendFilterPeriod,
        UseTrendFilter = UseTrendFilter,
        VolumePeriod = VolumePeriod,
        VolumeThreshold = VolumeThreshold,
        RequireVolumeConfirmation = RequireVolumeConfirmation
    };

    public static RsiStrategyConfigSettings FromSettings(RsiStrategySettings settings) => new()
    {
        RsiPeriod = settings.RsiPeriod,
        OversoldLevel = settings.OversoldLevel,
        OverboughtLevel = settings.OverboughtLevel,
        NeutralZoneLow = settings.NeutralZoneLow,
        NeutralZoneHigh = settings.NeutralZoneHigh,
        ExitOnNeutral = settings.ExitOnNeutral,
        AtrPeriod = settings.AtrPeriod,
        AtrStopMultiplier = settings.AtrStopMultiplier,
        TakeProfitMultiplier = settings.TakeProfitMultiplier,
        TrendFilterPeriod = settings.TrendFilterPeriod,
        UseTrendFilter = settings.UseTrendFilter,
        VolumePeriod = settings.VolumePeriod,
        VolumeThreshold = settings.VolumeThreshold,
        RequireVolumeConfirmation = settings.RequireVolumeConfirmation
    };
}
