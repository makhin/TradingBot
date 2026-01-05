using ComplexBot.Services.Strategies;

namespace ComplexBot.Configuration;

public class MaStrategyConfigSettings
{
    public int FastMaPeriod { get; set; } = 10;
    public int SlowMaPeriod { get; set; } = 30;
    public int AtrPeriod { get; set; } = 14;
    public decimal AtrStopMultiplier { get; set; } = 2.0m;
    public decimal TakeProfitMultiplier { get; set; } = 2.0m;
    public int VolumePeriod { get; set; } = 20;
    public decimal VolumeThreshold { get; set; } = 1.2m;
    public bool RequireVolumeConfirmation { get; set; } = true;

    public MaStrategySettings ToMaStrategySettings() => new()
    {
        FastMaPeriod = FastMaPeriod,
        SlowMaPeriod = SlowMaPeriod,
        AtrPeriod = AtrPeriod,
        AtrStopMultiplier = AtrStopMultiplier,
        TakeProfitMultiplier = TakeProfitMultiplier,
        VolumePeriod = VolumePeriod,
        VolumeThreshold = VolumeThreshold,
        RequireVolumeConfirmation = RequireVolumeConfirmation
    };

    public static MaStrategyConfigSettings FromSettings(MaStrategySettings settings) => new()
    {
        FastMaPeriod = settings.FastMaPeriod,
        SlowMaPeriod = settings.SlowMaPeriod,
        AtrPeriod = settings.AtrPeriod,
        AtrStopMultiplier = settings.AtrStopMultiplier,
        TakeProfitMultiplier = settings.TakeProfitMultiplier,
        VolumePeriod = settings.VolumePeriod,
        VolumeThreshold = settings.VolumeThreshold,
        RequireVolumeConfirmation = settings.RequireVolumeConfirmation
    };
}
