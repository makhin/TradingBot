using ComplexBot.Services.Backtesting;

namespace ComplexBot.Configuration.Optimization;

public class RsiOptimizerConfigSettings
{
    public int RsiPeriodMin { get; set; } = 10;
    public int RsiPeriodMax { get; set; } = 20;
    public decimal OversoldMin { get; set; } = 20m;
    public decimal OversoldMax { get; set; } = 35m;
    public decimal OverboughtMin { get; set; } = 65m;
    public decimal OverboughtMax { get; set; } = 80m;
    public decimal NeutralZoneLow { get; set; } = 45m;
    public decimal NeutralZoneHigh { get; set; } = 55m;
    public int AtrPeriod { get; set; } = 14;
    public decimal AtrMultiplierMin { get; set; } = 1.0m;
    public decimal AtrMultiplierMax { get; set; } = 3.5m;
    public decimal TakeProfitMultiplierMin { get; set; } = 1.5m;
    public decimal TakeProfitMultiplierMax { get; set; } = 3.0m;
    public int TrendFilterMin { get; set; } = 20;
    public int TrendFilterMax { get; set; } = 100;
    public int VolumePeriod { get; set; } = 20;
    public decimal VolumeThresholdMin { get; set; } = 1.0m;
    public decimal VolumeThresholdMax { get; set; } = 2.5m;

    public RsiOptimizerConfig ToRsiOptimizerConfig() => new()
    {
        RsiPeriodMin = RsiPeriodMin,
        RsiPeriodMax = RsiPeriodMax,
        OversoldMin = OversoldMin,
        OversoldMax = OversoldMax,
        OverboughtMin = OverboughtMin,
        OverboughtMax = OverboughtMax,
        NeutralZoneLow = NeutralZoneLow,
        NeutralZoneHigh = NeutralZoneHigh,
        AtrPeriod = AtrPeriod,
        AtrMultiplierMin = AtrMultiplierMin,
        AtrMultiplierMax = AtrMultiplierMax,
        TakeProfitMultiplierMin = TakeProfitMultiplierMin,
        TakeProfitMultiplierMax = TakeProfitMultiplierMax,
        TrendFilterMin = TrendFilterMin,
        TrendFilterMax = TrendFilterMax,
        VolumePeriod = VolumePeriod,
        VolumeThresholdMin = VolumeThresholdMin,
        VolumeThresholdMax = VolumeThresholdMax
    };
}
