namespace SignalBot.Configuration;

/// <summary>
/// Retry settings for Telegram command bot long-polling.
/// </summary>
public class TelegramCommandRetrySettings
{
    /// <summary>
    /// Base delay (in seconds) before retrying after a polling error.
    /// </summary>
    public int BaseDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Maximum delay (in seconds) between retries.
    /// </summary>
    public int MaxDelaySeconds { get; set; } = 60;
}
