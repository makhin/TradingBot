using System.Text.RegularExpressions;

namespace SignalBot.Services.Telegram;

public sealed class RiskOrderSignalParser : SignalMessageParserBase
{
    public override string Name => "risk-order";

    private static readonly Regex ParserRegex = new Regex(
        @"(?<direction>Long|Short)\s*-\s*\$(?<symbol>[A-Za-z0-9]+)\s*.*?
          Entry\s*1:\s*(?<entry1>[\d.]+)\s*.*?
          (?:Entry\s*2:\s*(?<entry2>[\d.]+)\s*)?.*?
          (?:SL|Stop\s*Loss)\s*:\s*(?<sl>[\d.]+)\s*.*?
          (?:TP\s*\d+\s*:\s*(?<target>[\d.]+)\s*)+",
        CommonRegexOptions);

    protected override Regex Pattern => ParserRegex;
}
