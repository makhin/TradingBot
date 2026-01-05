namespace ComplexBot.Services.Backtesting;

/// <summary>
/// Parameter ranges for ADX strategy optimization
/// </summary>
public record AdxOptimizerConfig
{
    public int AdxPeriodMin { get; init; } = 10;
    public int AdxPeriodMax { get; init; } = 25;
    public decimal AdxThresholdMin { get; init; } = 18m;
    public decimal AdxThresholdMax { get; init; } = 35m;
    public decimal AdxExitThresholdMin { get; init; } = 12m;
    public decimal AdxExitThresholdMax { get; init; } = 25m;
    public int FastEmaMin { get; init; } = 8;
    public int FastEmaMax { get; init; } = 30;
    public int SlowEmaMin { get; init; } = 35;
    public int SlowEmaMax { get; init; } = 100;
    public decimal AtrMultiplierMin { get; init; } = 1.5m;
    public decimal AtrMultiplierMax { get; init; } = 4.0m;
    public decimal TakeProfitMultiplierMin { get; init; } = 1.0m;
    public decimal TakeProfitMultiplierMax { get; init; } = 3.0m;
    public decimal VolumeThresholdMin { get; init; } = 1.0m;
    public decimal VolumeThresholdMax { get; init; } = 2.5m;
}
