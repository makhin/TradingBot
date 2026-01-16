using System.Diagnostics;
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
public class SignalParser
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, ISignalMessageParser> _parsers;
    private readonly Dictionary<long, string> _channelParsers;
    private readonly HashSet<string> _missingParsersLogged;
    private readonly string _defaultParserName;
    private readonly int _defaultLeverage;
    private readonly Dictionary<string, int> _parserDefaultLeverages;

    public SignalParser(
        TelegramSettings settings,
        IEnumerable<ISignalMessageParser> parsers,
        ILogger? logger = null)
    {
        _logger = logger ?? Log.ForContext<SignalParser>();
        _parsers = new Dictionary<string, ISignalMessageParser>(StringComparer.OrdinalIgnoreCase);
        _channelParsers = new Dictionary<long, string>();
        _missingParsersLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var parser in parsers)
        {
            if (string.IsNullOrWhiteSpace(parser.Name))
            {
                _logger.Warning("Parser with empty name ignored: {ParserType}", parser.GetType().Name);
                continue;
            }

            if (_parsers.TryGetValue(parser.Name, out var existing))
            {
                _logger.Warning(
                    "Duplicate parser name '{ParserName}' ({ExistingType} -> {NewType}). Using the last one.",
                    parser.Name,
                    existing.GetType().Name,
                    parser.GetType().Name);
            }

            _parsers[parser.Name] = parser;
        }

        if (_parsers.Count == 0)
        {
            _logger.Warning("No parsers registered. Signal parsing will fail until parsers are registered.");
        }

        var parsing = settings.Parsing ?? new TelegramParsingSettings();
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

        var defaultParserName = parsing.DefaultParser?.Trim();
        if (string.IsNullOrWhiteSpace(defaultParserName))
        {
            _logger.Warning(
                "Default parser is not configured. Parsing will fail unless channel parsers are mapped.");
            defaultParserName = string.Empty;
        }
        else if (!_parsers.ContainsKey(defaultParserName))
        {
            _logger.Warning(
                "Default parser '{ParserName}' not registered.",
                defaultParserName);
            defaultParserName = string.Empty;
        }

        _defaultParserName = defaultParserName;
        _defaultLeverage = parsing.DefaultLeverage > 0 ? parsing.DefaultLeverage : 1;
        _parserDefaultLeverages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var parserDefault in parsing.ParserDefaultLeverages)
        {
            var name = parserDefault.Key?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (parserDefault.Value <= 0)
            {
                _logger.Warning(
                    "Parser default leverage for {ParserName} is invalid: {Leverage}",
                    name,
                    parserDefault.Value);
                continue;
            }

            _parserDefaultLeverages[name] = parserDefault.Value;
        }
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

            var parser = GetParser(parserName);
            if (parser is null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Parser not configured");
                _logger.Warning(
                    "Parser not configured for channel {ChannelId}: {ParserName}",
                    source.ChannelId,
                    parserName);
                return SignalParserResult.Failed("Parser not configured");
            }

            var defaultLeverage = GetDefaultLeverage(parserName);
            var result = parser.Parse(normalizedText, source, defaultLeverage);
            if (!result.IsSuccess || result.Signal is null)
            {
                var error = result.ErrorMessage ?? "Signal format not recognized";
                activity?.SetStatus(ActivityStatusCode.Error, error);
                _logger.Warning(
                    "Signal parse failed by parser {ParserName}: {Error}. Text: {Text}",
                    parserName,
                    error,
                    TruncateText(normalizedText, 100));
                return SignalParserResult.Failed(error);
            }

            var signal = result.Signal;

            activity?.SetTag("signal.id", signal.Id);
            activity?.SetTag("signal.symbol", signal.Symbol);
            activity?.SetTag("signal.direction", signal.Direction.ToString());

            using (LogContext.PushProperty("SignalId", signal.Id))
            {
                _logger.Information(
                    "Signal parsed by {ParserName}: {Symbol} {Direction} @ {Entry}, SL: {SL}, Leverage: {Leverage}x, Targets: {Targets}",
                    parserName,
                    signal.Symbol,
                    signal.Direction,
                    signal.Entry,
                    signal.OriginalStopLoss,
                    signal.OriginalLeverage,
                    signal.Targets.Count);
            }

            return result;
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

    private ISignalMessageParser? GetParser(string parserName)
    {
        if (string.IsNullOrWhiteSpace(parserName))
        {
            if (_missingParsersLogged.Add("<empty>"))
            {
                _logger.Warning("Parser not configured. Set a default parser or channel mapping.");
            }

            return null;
        }

        if (_parsers.TryGetValue(parserName, out var parser))
        {
            return parser;
        }

        if (_missingParsersLogged.Add(parserName))
        {
            _logger.Warning("Parser '{ParserName}' not registered.", parserName);
        }

        return null;
    }

    private int GetDefaultLeverage(string parserName)
    {
        if (_parserDefaultLeverages.TryGetValue(parserName, out var leverage) && leverage > 0)
        {
            return leverage;
        }

        return _defaultLeverage;
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

    /// <summary>
    /// Updates channel-to-parser mappings with resolved channel IDs.
    /// Called by TelegramSignalListener after resolving channel names.
    /// </summary>
    public void UpdateChannelMappings(IEnumerable<TelegramChannelParserSettings> mappings)
    {
        foreach (var mapping in mappings)
        {
            var effectiveId = mapping.GetEffectiveChannelId();
            if (effectiveId == 0 || string.IsNullOrWhiteSpace(mapping.Parser))
                continue;

            var parserName = mapping.Parser.Trim();
            AddChannelParser(effectiveId, parserName);

            // Also add API format ID for matching
            var apiId = TelegramIdHelper.ConvertToApiFormat(effectiveId);
            if (apiId != effectiveId)
            {
                AddChannelParser(apiId, parserName);
            }

            _logger.Debug(
                "Channel mapping updated: {ChannelId} -> {Parser}",
                effectiveId, parserName);
        }
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
