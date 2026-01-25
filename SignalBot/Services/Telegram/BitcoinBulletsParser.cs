using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SignalBot.Configuration;

namespace SignalBot.Services.Telegram;

public sealed class BitcoinBulletsParser : SignalMessageParserBase
{
    public override string Name => "bitcoin-bullets";

    public BitcoinBulletsParser(IOptions<SignalBotSettings> settings)
        : base(settings.Value.Trading.DefaultSymbolSuffix)
    {
    }

    private static readonly Regex ParserRegex = new Regex(
        @"Coin\s*:\s*\#(?<symbol>[A-Za-z0-9]+)/USDT\s*.*?
          (?<direction>LONG|SHORT)\s*.*?
          .*?Entry:\s*(?<entry1>[\d.]+)\s*(?:-\s*(?<entry2>[\d.]+))?\s*.*?
          .*?Leverage:\s*(?<leverage>\d+)x\s*.*?
          (?:.*?Target\s*1:\s*(?<t1>[\d.]+)\s*)?
          (?:.*?Target\s*2:\s*(?<t2>[\d.]+)\s*)?
          (?:.*?Target\s*3:\s*(?<t3>[\d.]+)\s*)?
          (?:.*?Target\s*4:\s*(?<t4>[\d.]+)\s*)?
          (?:.*?Target\s*5:\s*(?<t5>[\d.]+)\s*)?
          (?:.*?Target\s*6:\s*(?<t6>[\d.]+)\s*)?
          (?:.*?Target\s*7:\s*(?<t7>[\d.]+)\s*)?
          (?:.*?Target\s*8:\s*(?<t8>[\d.]+)\s*)?
          (?:.*?Target\s*9:\s*(?<t9>[\d.]+)\s*)?
          (?:.*?Target\s*10:\s*(?<t10>[\d.]+)\s*)?.*?
          .*?StopLoss:\s*(?<sl>[\d.]+)",
        CommonRegexOptions);

    protected override Regex Pattern => ParserRegex;
}
