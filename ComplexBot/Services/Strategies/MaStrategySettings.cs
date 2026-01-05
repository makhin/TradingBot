namespace ComplexBot.Services.Strategies;

public record MaStrategySettings
{
    public int FastMaPeriod { get; init; } = 10;
    public int SlowMaPeriod { get; init; } = 30;
    public int AtrPeriod { get; init; } = 14;
    public decimal AtrStopMultiplier { get; init; } = 2.0m;
    public decimal TakeProfitMultiplier { get; init; } = 2.0m;
    public int VolumePeriod { get; init; } = 20;
    public decimal VolumeThreshold { get; init; } = 1.2m;
    public bool RequireVolumeConfirmation { get; init; } = true;
}
