using ComplexBot.Services.Backtesting;

namespace ComplexBot.Configuration;

public class AdxOptimizerConfigSettings
{
    public int AdxPeriodMin { get; set; } = 10;
    public int AdxPeriodMax { get; set; } = 25;
    public decimal AdxThresholdMin { get; set; } = 18m;
    public decimal AdxThresholdMax { get; set; } = 35m;
    public decimal AdxExitThresholdMin { get; set; } = 12m;
    public decimal AdxExitThresholdMax { get; set; } = 25m;
    public int FastEmaMin { get; set; } = 8;
    public int FastEmaMax { get; set; } = 30;
    public int SlowEmaMin { get; set; } = 35;
    public int SlowEmaMax { get; set; } = 100;
    public decimal AtrMultiplierMin { get; set; } = 1.5m;
    public decimal AtrMultiplierMax { get; set; } = 4.0m;
    public decimal TakeProfitMultiplierMin { get; set; } = 1.0m;
    public decimal TakeProfitMultiplierMax { get; set; } = 3.0m;
    public decimal VolumeThresholdMin { get; set; } = 1.0m;
    public decimal VolumeThresholdMax { get; set; } = 2.5m;

    public AdxOptimizerConfig ToAdxOptimizerConfig() => new()
    {
        AdxPeriodMin = AdxPeriodMin,
        AdxPeriodMax = AdxPeriodMax,
        AdxThresholdMin = AdxThresholdMin,
        AdxThresholdMax = AdxThresholdMax,
        AdxExitThresholdMin = AdxExitThresholdMin,
        AdxExitThresholdMax = AdxExitThresholdMax,
        FastEmaMin = FastEmaMin,
        FastEmaMax = FastEmaMax,
        SlowEmaMin = SlowEmaMin,
        SlowEmaMax = SlowEmaMax,
        AtrMultiplierMin = AtrMultiplierMin,
        AtrMultiplierMax = AtrMultiplierMax,
        TakeProfitMultiplierMin = TakeProfitMultiplierMin,
        TakeProfitMultiplierMax = TakeProfitMultiplierMax,
        VolumeThresholdMin = VolumeThresholdMin,
        VolumeThresholdMax = VolumeThresholdMax
    };
}
