namespace ComplexBot.Services.Backtesting;

public record RsiOptimizerConfig
{
    public int RsiPeriodMin { get; init; } = 10;
    public int RsiPeriodMax { get; init; } = 20;
    public decimal OversoldMin { get; init; } = 20m;
    public decimal OversoldMax { get; init; } = 35m;
    public decimal OverboughtMin { get; init; } = 65m;
    public decimal OverboughtMax { get; init; } = 80m;
    public decimal NeutralZoneLow { get; init; } = 45m;
    public decimal NeutralZoneHigh { get; init; } = 55m;
    public int AtrPeriod { get; init; } = 14;
    public decimal AtrMultiplierMin { get; init; } = 1.0m;
    public decimal AtrMultiplierMax { get; init; } = 3.5m;
    public decimal TakeProfitMultiplierMin { get; init; } = 1.5m;
    public decimal TakeProfitMultiplierMax { get; init; } = 3.0m;
    public int TrendFilterMin { get; init; } = 20;
    public int TrendFilterMax { get; init; } = 100;
    public int VolumePeriod { get; init; } = 20;
    public decimal VolumeThresholdMin { get; init; } = 1.0m;
    public decimal VolumeThresholdMax { get; init; } = 2.5m;
}
