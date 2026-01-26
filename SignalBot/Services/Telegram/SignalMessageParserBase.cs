using System.Globalization;
using System.Text.RegularExpressions;
using SignalBot.Models;

namespace SignalBot.Services.Telegram;

public abstract class SignalMessageParserBase : ISignalMessageParser
{
    protected const RegexOptions CommonRegexOptions =
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace;

    private readonly string _symbolSuffix;

    protected abstract Regex Pattern { get; }
    public abstract string Name { get; }

    protected SignalMessageParserBase(string symbolSuffix)
    {
        _symbolSuffix = string.IsNullOrWhiteSpace(symbolSuffix)
            ? "USDT"
            : symbolSuffix.Trim().ToUpperInvariant();
    }

    public SignalParserResult Parse(string text, SignalSource source, int defaultLeverage)
    {
        var normalizedText = text.Trim();
        var match = Pattern.Match(normalizedText);

        if (!match.Success)
        {
            return SignalParserResult.Failed("Signal format not recognized");
        }

        var targets = ParseTargets(match);
        if (targets.Count == 0)
        {
            return SignalParserResult.Failed("No targets found in signal");
        }

        var directionStr = match.Groups["direction"].Value;
        var direction = directionStr.Equals("Long", StringComparison.OrdinalIgnoreCase)
            ? SignalDirection.Long
            : SignalDirection.Short;

        if (!TryParseEntry(match, out var entry))
        {
            return SignalParserResult.Failed("Invalid entry price");
        }

        if (!decimal.TryParse(
                match.Groups["sl"].Value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var stopLoss))
        {
            return SignalParserResult.Failed("Invalid stop loss price");
        }

        if (!TryParseLeverage(match, defaultLeverage, out var leverage))
        {
            return SignalParserResult.Failed("Invalid leverage");
        }

        var symbol = BuildSymbol(match.Groups["symbol"].Value);

        var signal = new TradingSignal
        {
            RawText = normalizedText,
            Source = source,
            Symbol = symbol,
            Direction = direction,
            Entry = entry,
            OriginalStopLoss = stopLoss,
            Targets = targets,
            OriginalLeverage = leverage
        };

        return SignalParserResult.Success(signal);
    }

    protected virtual string BuildSymbol(string rawSymbol)
    {
        return rawSymbol.ToUpperInvariant() + _symbolSuffix;
    }

    private static bool TryParseEntry(Match match, out decimal entry)
    {
        if (TryParseDecimalGroup(match, "entry", out entry, out var hasEntry))
        {
            return true;
        }

        if (hasEntry)
        {
            return false;
        }

        var entry1Parsed = TryParseDecimalGroup(match, "entry1", out var entry1, out var hasEntry1);
        if (hasEntry1 && !entry1Parsed)
        {
            entry = default;
            return false;
        }

        var entry2Parsed = TryParseDecimalGroup(match, "entry2", out var entry2, out var hasEntry2);
        if (hasEntry2 && !entry2Parsed)
        {
            entry = default;
            return false;
        }

        if (entry1Parsed && entry2Parsed)
        {
            entry = (entry1 + entry2) / 2m;
            return true;
        }

        if (entry1Parsed)
        {
            entry = entry1;
            return true;
        }

        if (entry2Parsed)
        {
            entry = entry2;
            return true;
        }

        entry = default;
        return false;
    }

    private static bool TryParseLeverage(Match match, int defaultLeverage, out int leverage)
    {
        var group = match.Groups["leverage"];
        if (group.Success && !string.IsNullOrWhiteSpace(group.Value))
        {
            if (int.TryParse(group.Value, out leverage) && leverage > 0)
            {
                return true;
            }

            leverage = default;
            return false;
        }

        leverage = defaultLeverage > 0 ? defaultLeverage : 1;
        return true;
    }

    private static List<decimal> ParseTargets(Match match)
    {
        var targets = new List<decimal>();
        var targetGroup = match.Groups["target"];
        if (targetGroup.Success && targetGroup.Captures.Count > 0)
        {
            foreach (Capture capture in targetGroup.Captures)
            {
                if (decimal.TryParse(
                        capture.Value,
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out var target))
                {
                    targets.Add(target);
                }
            }

            return targets;
        }

        for (var i = 1; i <= 10; i++)
        {
            var group = match.Groups[$"t{i}"];
            if (group.Success &&
                decimal.TryParse(
                    group.Value,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var target))
            {
                targets.Add(target);
            }
        }

        return targets;
    }

    private static bool TryParseDecimalGroup(
        Match match,
        string groupName,
        out decimal value,
        out bool hasValue)
    {
        var group = match.Groups[groupName];
        if (group.Success && !string.IsNullOrWhiteSpace(group.Value))
        {
            hasValue = true;
            return decimal.TryParse(group.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        value = default;
        hasValue = false;
        return false;
    }
}
