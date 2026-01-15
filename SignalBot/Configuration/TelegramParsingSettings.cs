namespace SignalBot.Configuration;

/// <summary>
/// Telegram signal parsing configuration
/// </summary>
public class TelegramParsingSettings
{
    public string DefaultParser { get; set; } = "default";
    public Dictionary<string, string> Parsers { get; set; } = new();
    public List<TelegramChannelParserSettings> ChannelParsers { get; set; } = new();
}
