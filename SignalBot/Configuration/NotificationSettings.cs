namespace SignalBot.Configuration;

/// <summary>
/// Notification settings
/// </summary>
public class NotificationSettings
{
    public string TelegramBotToken { get; set; } = string.Empty;
    public string TelegramChatId { get; set; } = string.Empty;
    public List<long> TelegramAuthorizedUserIds { get; set; } = new();

    public TelegramCommandRetrySettings TelegramCommandRetry { get; set; } = new();

    public bool NotifyOnSignalReceived { get; set; } = true;
    public bool NotifyOnPositionOpened { get; set; } = true;
    public bool NotifyOnTargetHit { get; set; } = true;
    public bool NotifyOnPositionClosed { get; set; } = true;
    public bool NotifyOnError { get; set; } = true;
}
