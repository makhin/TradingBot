namespace ComplexBot.Configuration;

public class TelegramSettings
{
    public bool Enabled { get; set; } = false;
    public string BotToken { get; set; } = string.Empty;
    public long ChatId { get; set; } = 0;
}
