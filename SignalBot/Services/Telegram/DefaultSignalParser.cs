using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SignalBot.Configuration;

namespace SignalBot.Services.Telegram;

public sealed class DefaultSignalParser : SignalMessageParserBase
{
    public override string Name => "default";

    public DefaultSignalParser()
        : base("USDT")
    {
    }

    public DefaultSignalParser(IOptions<SignalBotSettings> settings)
        : base(settings.Value.Trading.DefaultSymbolSuffix)
    {
    }

    private static readonly Regex ParserRegex = new Regex(
        @"\#(?<symbol>\w+)/USDT\s*-\s*(?<direction>Long|Short)\s*
          (?:\uD83D\uDFE2|\uD83D\uDD34)?\s*
          Entry:\s*(?<entry>[\d.]+)\s*
          Stop\s*Loss:\s*(?<sl>[\d.]+)\s*
          (?:Target\s*1:\s*(?<t1>[\d.]+)\s*)?
          (?:Target\s*2:\s*(?<t2>[\d.]+)\s*)?
          (?:Target\s*3:\s*(?<t3>[\d.]+)\s*)?
          (?:Target\s*4:\s*(?<t4>[\d.]+)\s*)?
          (?:Target\s*5:\s*(?<t5>[\d.]+)\s*)?
          (?:Target\s*6:\s*(?<t6>[\d.]+)\s*)?
          (?:Target\s*7:\s*(?<t7>[\d.]+)\s*)?
          (?:Target\s*8:\s*(?<t8>[\d.]+)\s*)?
          (?:Target\s*9:\s*(?<t9>[\d.]+)\s*)?
          (?:Target\s*10:\s*(?<t10>[\d.]+)\s*)?
          Leverage:\s*x(?<leverage>\d+)",
        CommonRegexOptions);

    protected override Regex Pattern => ParserRegex;
}
