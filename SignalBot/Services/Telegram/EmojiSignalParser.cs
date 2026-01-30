using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SignalBot.Configuration;

namespace SignalBot.Services.Telegram;

public sealed class EmojiSignalParser : SignalMessageParserBase
{
    public override string Name => "emoji-signal";

    public EmojiSignalParser(IOptions<SignalBotSettings> settings)
        : base(settings.Value.Trading.SignalSymbolSuffix)
    {
    }

    private static readonly Regex ParserRegex = new Regex(
        @"(?:\uD83D\uDFE2|\uD83D\uDD34)?\s*
          (?<direction>LONG|SHORT)\s*-\s*\$?(?<symbol>[A-Za-z0-9]+)\s*
          .*?-?\s*Entry:\s*(?<entry>[\d.]+)\s*
          .*?-?\s*SL:\s*(?<sl>[\d.]+)\s*
          (?:.*?(?:\uD83C\uDFAF\s*)?TP\s*\d+\s*:\s*(?<target>[\d.]+)\s*)+",
        CommonRegexOptions);

    protected override Regex Pattern => ParserRegex;
}
