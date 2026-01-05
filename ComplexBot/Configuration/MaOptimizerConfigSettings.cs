using ComplexBot.Services.Backtesting;

namespace ComplexBot.Configuration;

public class MaOptimizerConfigSettings
{
    public int FastMaMin { get; set; } = 5;
    public int FastMaMax { get; set; } = 25;
    public int SlowMaMin { get; set; } = 20;
    public int SlowMaMax { get; set; } = 120;
    public int AtrPeriod { get; set; } = 14;
    public decimal AtrMultiplierMin { get; set; } = 1.5m;
    public decimal AtrMultiplierMax { get; set; } = 4.0m;
    public decimal TakeProfitMultiplierMin { get; set; } = 1.0m;
    public decimal TakeProfitMultiplierMax { get; set; } = 3.0m;
    public int VolumePeriod { get; set; } = 20;
    public decimal VolumeThresholdMin { get; set; } = 1.0m;
    public decimal VolumeThresholdMax { get; set; } = 2.5m;

    public MaOptimizerConfig ToMaOptimizerConfig() => new()
    {
        FastMaMin = FastMaMin,
        FastMaMax = FastMaMax,
        SlowMaMin = SlowMaMin,
        SlowMaMax = SlowMaMax,
        AtrPeriod = AtrPeriod,
        AtrMultiplierMin = AtrMultiplierMin,
        AtrMultiplierMax = AtrMultiplierMax,
        TakeProfitMultiplierMin = TakeProfitMultiplierMin,
        TakeProfitMultiplierMax = TakeProfitMultiplierMax,
        VolumePeriod = VolumePeriod,
        VolumeThresholdMin = VolumeThresholdMin,
        VolumeThresholdMax = VolumeThresholdMax
    };
}
