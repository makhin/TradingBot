namespace SignalBot.Configuration;

/// <summary>
/// Cooldown settings after losses
/// </summary>
public class CooldownSettings
{
    public bool Enabled { get; set; } = true;
    public TimeSpan CooldownAfterStopLoss { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan CooldownAfterLiquidation { get; set; } = TimeSpan.FromHours(1);
    public int ConsecutiveLossesForLongCooldown { get; set; } = 3;
    public TimeSpan LongCooldownDuration { get; set; } = TimeSpan.FromHours(2);

    public bool ReduceSizeAfterLosses { get; set; } = true;
    public decimal SizeMultiplierAfter1Loss { get; set; } = 0.75m;
    public decimal SizeMultiplierAfter2Losses { get; set; } = 0.5m;
    public decimal SizeMultiplierAfter3PlusLosses { get; set; } = 0.25m;
}
