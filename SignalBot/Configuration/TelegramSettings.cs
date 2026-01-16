namespace SignalBot.Configuration;

/// <summary>
/// Telegram connection settings
/// </summary>
public class TelegramSettings
{
    public int ApiId { get; set; }
    public string ApiHash { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string SessionPath { get; set; } = "telegram_session.dat";

    /// <summary>
    /// Minimum log level for WTelegram client logs (Trace/Debug/Information/Warning/Error/Critical/None)
    /// </summary>
    public string ClientLogLevel { get; set; } = "Warning";

    /// <summary>
    /// Parser configuration for Telegram signals
    /// </summary>
    public TelegramParsingSettings Parsing { get; set; } = new();
}
