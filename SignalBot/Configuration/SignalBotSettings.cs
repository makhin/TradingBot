namespace SignalBot.Configuration;

/// <summary>
/// Main SignalBot settings container
/// </summary>
public class SignalBotSettings
{
    public TelegramSettings Telegram { get; set; } = new();
    public TradingSettings Trading { get; set; } = new();
    public DuplicateHandlingSettings DuplicateHandling { get; set; } = new();
    public PositionSizingSettings PositionSizing { get; set; } = new();
    public EntrySettings Entry { get; set; } = new();
    public CooldownSettings Cooldown { get; set; } = new();
    public EmergencySettings Emergency { get; set; } = new();
    public RiskOverrideSettings RiskOverride { get; set; } = new();
    public NotificationSettings Notifications { get; set; } = new();
    public StateSettings State { get; set; } = new();
}
