namespace SignalBot.Configuration;

/// <summary>
/// Main SignalBot settings container
/// </summary>
public class SignalBotSettings
{
    public ExchangeSettings Exchange { get; set; } = new();
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
    public TradeStatisticsSettings Statistics { get; set; } = new();

    /// <summary>
    /// Enable/disable Futures trading. If disabled, only monitoring is available
    /// </summary>
    public bool EnableFuturesTrading { get; set; } = true;
}
