using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using SignalBot.Configuration;
using SignalBot.Models;
using SignalBot.Telemetry;
using SignalBot.Utils;
using Serilog;
using Serilog.Context;

namespace SignalBot.Services.Telegram;

/// <summary>
/// Parses trading signals from Telegram messages
/// </summary>
public partial class SignalParser
{
    private const string DefaultParserName = "default";
    private const RegexOptions ParserRegexOptions = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled;

    private readonly ILogger _logger;
    private readonly Dictionary<string, Regex> _parsers;
    private readonly Dictionary<long, string> _channelParsers;
    private readonly HashSet<string> _missingParsersLogged;
    private readonly string _defaultParserName;

    // Regex pattern for signal parsing (supports up to 10 targets)
    [GeneratedRegex(
        @"#(?<symbol>\w+)/USDT\s*-\s*(?<direction>Long|Short)\s*(?:🟢|🔴)?\s*" +
        @"Entry:\s*(?<entry>[\d.]+)\s*" +
        @"Stop\s*Loss:\s*(?<sl>[\d.]+)\s*" +
        @"(?:Target\s*1:\s*(?<t1>[\d.]+)\s*)?" +
        @"(?:Target\s*2:\s*(?<t2>[\d.]+)\s*)?" +
        @"(?:Target\s*3:\s*(?<t3>[\d.]+)\s*)?" +
        @"(?:Target\s*4:\s*(?<t4>[\d.]+)\s*)?" +
        @"(?:Target\s*5:\s*(?<t5>[\d.]+)\s*)?" +
        @"(?:Target\s*6:\s*(?<t6>[\d.]+)\s*)?" +
        @"(?:Target\s*7:\s*(?<t7>[\d.]+)\s*)?" +
        @"(?:Target\s*8:\s*(?<t8>[\d.]+)\s*)?" +
        @"(?:Target\s*9:\s*(?<t9>[\d.]+)\s*)?" +
        @"(?:Target\s*10:\s*(?<t10>[\d.]+)\s*)?" +
        @"Leverage:\s*x(?<leverage>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex SignalRegex();

    public SignalParser(TelegramSettings settings, ILogger? logger = null)
    {
        _logger = logger ?? Log.ForContext<SignalParser>();
        _parsers = new Dictionary<string, Regex>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultParserName] = SignalRegex()
        };
        _channelParsers = new Dictionary<long, string>();
        _missingParsersLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var parsing = settings.Parsing ?? new TelegramParsingSettings();
        foreach (var parser in parsing.Parsers)
        {
            var name = parser.Key?.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parser.Value))
            {
                continue;
            }

            try
            {
                _parsers[name] = new Regex(parser.Value, ParserRegexOptions);
            }
            catch (ArgumentException ex)
            {
                _logger.Warning(ex, "Invalid regex pattern for parser {ParserName}", name);
            }
        }

        foreach (var mapping in parsing.ChannelParsers)
        {
            if (mapping.ChannelId == 0 || string.IsNullOrWhiteSpace(mapping.Parser))
            {
                continue;
            }

            var parserName = mapping.Parser.Trim();
            AddChannelParser(mapping.ChannelId, parserName);

            var apiId = TelegramIdHelper.ConvertToApiFormat(mapping.ChannelId);
            if (apiId != mapping.ChannelId)
            {
                AddChannelParser(apiId, parserName);
            }
        }

        var defaultParserName = string.IsNullOrWhiteSpace(parsing.DefaultParser)
            ? DefaultParserName
            : parsing.DefaultParser.Trim();

        if (!_parsers.ContainsKey(defaultParserName))
        {
            _logger.Warning(
                "Default parser '{ParserName}' not found. Falling back to '{FallbackParser}'.",
                defaultParserName,
                DefaultParserName);
            defaultParserName = DefaultParserName;
        }

        _defaultParserName = defaultParserName;
    }

    public SignalParserResult Parse(string text, SignalSource source)
    {
        try
        {
            using var activity = SignalBotTelemetry.ActivitySource.StartActivity("Parse", ActivityKind.Internal);
            activity?.SetTag("signal.source.channel", source.ChannelName);
            activity?.SetTag("signal.source.channel_id", source.ChannelId);
            activity?.SetTag("signal.source.message_id", source.MessageId);

            var normalizedText = text.Trim();
            var parserName = ResolveParserName(source.ChannelId);
            activity?.SetTag("signal.parser", parserName);

            var regex = GetParserRegex(parserName);
            var match = regex.Match(normalizedText);

            if (!match.Success)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Signal format not recognized");
                _logger.Warning(
                    "Signal format not recognized by parser {ParserName}: {Text}",
                    parserName,
                    TruncateText(normalizedText, 100));
                return SignalParserResult.Failed("Signal format not recognized");
            }

            // Parse targets
            var targets = ParseTargets(match);

            if (targets.Count == 0)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "No targets found in signal");
                _logger.Warning(
                    "No targets found in signal by parser {ParserName}: {Text}",
                    parserName,
                    TruncateText(normalizedText, 100));
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
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid entry price");
                return SignalParserResult.Failed("Invalid entry price");
            }

            // Parse stop loss
            if (!decimal.TryParse(match.Groups["sl"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var stopLoss))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid stop loss price");
                return SignalParserResult.Failed("Invalid stop loss price");
            }

            // Parse leverage
            if (!int.TryParse(match.Groups["leverage"].Value, out var leverage))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid leverage");
                return SignalParserResult.Failed("Invalid leverage");
            }

            // Build symbol
            var symbol = match.Groups["symbol"].Value.ToUpperInvariant() + "USDT";

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

            activity?.SetTag("signal.id", signal.Id);
            activity?.SetTag("signal.symbol", signal.Symbol);
            activity?.SetTag("signal.direction", signal.Direction.ToString());

            using (LogContext.PushProperty("SignalId", signal.Id))
            {
                _logger.Information(
                    "Signal parsed by {ParserName}: {Symbol} {Direction} @ {Entry}, SL: {SL}, Leverage: {Leverage}x, Targets: {Targets}",
                    parserName, symbol, direction, entry, stopLoss, leverage, targets.Count);
            }

            return SignalParserResult.Success(signal);
        }
        catch (Exception ex)
        {
            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.Error(ex, "Error parsing signal: {Text}", TruncateText(text, 100));
            return SignalParserResult.Failed($"Parse error: {ex.Message}");
        }
    }

    private string ResolveParserName(long channelId)
    {
        return _channelParsers.TryGetValue(channelId, out var parserName) &&
               !string.IsNullOrWhiteSpace(parserName)
            ? parserName
            : _defaultParserName;
    }

    private Regex GetParserRegex(string parserName)
    {
        if (_parsers.TryGetValue(parserName, out var regex))
        {
            return regex;
        }

        if (_missingParsersLogged.Add(parserName))
        {
            _logger.Warning(
                "Parser '{ParserName}' not configured. Falling back to '{FallbackParser}'.",
                parserName,
                _defaultParserName);
        }

        if (_parsers.TryGetValue(_defaultParserName, out var fallback))
        {
            return fallback;
        }

        return _parsers[DefaultParserName];
    }

    private void AddChannelParser(long channelId, string parserName)
    {
        if (_channelParsers.TryGetValue(channelId, out var existing) &&
            !string.Equals(existing, parserName, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning(
                "Channel parser mapping for {ChannelId} overwritten: {OldParser} -> {NewParser}",
                channelId,
                existing,
                parserName);
        }

        _channelParsers[channelId] = parserName;
    }

    private static List<decimal> ParseTargets(Match match)
    {
        var targets = new List<decimal>();
        for (int i = 1; i <= 10; i++)
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
