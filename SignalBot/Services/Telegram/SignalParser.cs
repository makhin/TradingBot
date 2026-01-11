using System.Globalization;
using System.Text.RegularExpressions;
using SignalBot.Models;
using Serilog;

namespace SignalBot.Services.Telegram;

/// <summary>
/// Parses trading signals from Telegram messages
/// </summary>
public partial class SignalParser
{
    private readonly ILogger _logger;

    // Regex pattern for signal parsing
    [GeneratedRegex(
        @"#(?<symbol>\w+)/USDT\s*-\s*(?<direction>Long|Short)[🟢🔴]?\s*" +
        @"Entry:\s*(?<entry>[\d.]+)\s*" +
        @"Stop\s*Loss:\s*(?<sl>[\d.]+)\s*" +
        @"(?:Target\s*1:\s*(?<t1>[\d.]+)\s*)?" +
        @"(?:Target\s*2:\s*(?<t2>[\d.]+)\s*)?" +
        @"(?:Target\s*3:\s*(?<t3>[\d.]+)\s*)?" +
        @"(?:Target\s*4:\s*(?<t4>[\d.]+)\s*)?" +
        @"Leverage:\s*x(?<leverage>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex SignalRegex();

    public SignalParser(ILogger? logger = null)
    {
        _logger = logger ?? Log.ForContext<SignalParser>();
    }

    public SignalParserResult Parse(string text, SignalSource source)
    {
        try
        {
            var match = SignalRegex().Match(text);

            if (!match.Success)
            {
                _logger.Warning("Signal format not recognized: {Text}", TruncateText(text, 100));
                return SignalParserResult.Failed("Signal format not recognized");
            }

            // Parse targets
            var targets = ParseTargets(match);

            if (targets.Count == 0)
            {
                _logger.Warning("No targets found in signal: {Text}", TruncateText(text, 100));
                return SignalParserResult.Failed("No targets found in signal");
            }

            // Parse direction
            var directionStr = match.Groups["direction"].Value;
            var direction = directionStr.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? SignalDirection.Long
                : SignalDirection.Short;

            // Parse entry price
            if (!decimal.TryParse(match.Groups["entry"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var entry))
            {
                return SignalParserResult.Failed("Invalid entry price");
            }

            // Parse stop loss
            if (!decimal.TryParse(match.Groups["sl"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var stopLoss))
            {
                return SignalParserResult.Failed("Invalid stop loss price");
            }

            // Parse leverage
            if (!int.TryParse(match.Groups["leverage"].Value, out var leverage))
            {
                return SignalParserResult.Failed("Invalid leverage");
            }

            // Build symbol
            var symbol = match.Groups["symbol"].Value.ToUpperInvariant() + "USDT";

            var signal = new TradingSignal
            {
                RawText = text,
                Source = source,
                Symbol = symbol,
                Direction = direction,
                Entry = entry,
                OriginalStopLoss = stopLoss,
                Targets = targets,
                OriginalLeverage = leverage
            };

            _logger.Information(
                "Signal parsed: {Symbol} {Direction} @ {Entry}, SL: {SL}, Leverage: {Leverage}x, Targets: {Targets}",
                symbol, direction, entry, stopLoss, leverage, targets.Count);

            return SignalParserResult.Success(signal);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error parsing signal: {Text}", TruncateText(text, 100));
            return SignalParserResult.Failed($"Parse error: {ex.Message}");
        }
    }

    private static List<decimal> ParseTargets(Match match)
    {
        var targets = new List<decimal>();
        for (int i = 1; i <= 4; i++)
        {
            var group = match.Groups[$"t{i}"];
            if (group.Success && decimal.TryParse(group.Value,
                NumberStyles.Any, CultureInfo.InvariantCulture, out var target))
            {
                targets.Add(target);
            }
        }

        return targets;
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text.Substring(0, maxLength);
    }
}
