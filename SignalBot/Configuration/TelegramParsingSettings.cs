namespace SignalBot.Configuration;

/// <summary>
/// Telegram signal parsing configuration
/// </summary>
public class TelegramParsingSettings
{
    public string DefaultParser { get; set; } = string.Empty;
    public int DefaultLeverage { get; set; } = 1;
    public Dictionary<string, int> ParserDefaultLeverages { get; set; } = new();
    public List<TelegramChannelParserSettings> ChannelParsers { get; set; } = new();
}
