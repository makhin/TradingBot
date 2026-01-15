using SignalBot.Models;

namespace SignalBot.Services.Telegram;

public interface ISignalMessageParser
{
    string Name { get; }
    SignalParserResult Parse(string text, SignalSource source, int defaultLeverage);
}
