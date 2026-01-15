namespace SignalBot.Configuration;

/// <summary>
/// Per-channel parser mapping
/// </summary>
public class TelegramChannelParserSettings
{
    public long ChannelId { get; set; }
    public string Parser { get; set; } = string.Empty;
}
