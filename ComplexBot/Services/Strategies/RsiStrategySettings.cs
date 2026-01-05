namespace ComplexBot.Services.Strategies;

public record RsiStrategySettings
{
    public int RsiPeriod { get; init; } = 14;
    public decimal OversoldLevel { get; init; } = 30m;
    public decimal OverboughtLevel { get; init; } = 70m;
    public decimal NeutralZoneLow { get; init; } = 45m;
    public decimal NeutralZoneHigh { get; init; } = 55m;
    public bool ExitOnNeutral { get; init; } = false;
    public int AtrPeriod { get; init; } = 14;
    public decimal AtrStopMultiplier { get; init; } = 1.5m;
    public decimal TakeProfitMultiplier { get; init; } = 2.0m;
    public int TrendFilterPeriod { get; init; } = 50;
    public bool UseTrendFilter { get; init; } = true;
    public int VolumePeriod { get; init; } = 20;
    public decimal VolumeThreshold { get; init; } = 1.0m;
    public bool RequireVolumeConfirmation { get; init; } = false;
}
