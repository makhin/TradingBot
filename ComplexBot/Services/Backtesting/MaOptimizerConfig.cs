namespace ComplexBot.Services.Backtesting;

public record MaOptimizerConfig
{
    public int FastMaMin { get; init; } = 5;
    public int FastMaMax { get; init; } = 25;
    public int SlowMaMin { get; init; } = 20;
    public int SlowMaMax { get; init; } = 120;
    public int AtrPeriod { get; init; } = 14;
    public decimal AtrMultiplierMin { get; init; } = 1.5m;
    public decimal AtrMultiplierMax { get; init; } = 4.0m;
    public decimal TakeProfitMultiplierMin { get; init; } = 1.0m;
    public decimal TakeProfitMultiplierMax { get; init; } = 3.0m;
    public int VolumePeriod { get; init; } = 20;
    public decimal VolumeThresholdMin { get; init; } = 1.0m;
    public decimal VolumeThresholdMax { get; init; } = 2.5m;
}
