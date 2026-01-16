using SignalBot.Configuration;
using SignalBot.Models;
using SignalBot.Services;
using SignalBot.Utils;
using Serilog;
using Serilog.Events;
using TL;
using WTelegram;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace SignalBot.Services.Telegram;

/// <summary>
/// Telegram signal listener implementation using WTelegramClient
/// </summary>
public class TelegramSignalListener : ServiceBase, ITelegramSignalListener
{
    private readonly TelegramSettings _settings;
    private readonly SignalParser _parser;
    private readonly LogLevel _clientLogLevel;
    private Client? _client;
    private readonly HashSet<int> _processedMessageIds = new();
    private readonly SemaphoreSlim _messageLock = new(1, 1);

    // Resolved at startup after connecting to Telegram
    private HashSet<long> _monitoredChannelIds = new();
    private Dictionary<long, string> _channelNames = new();

    public event Action<TradingSignal>? OnSignalReceived;

    public bool IsListening => IsRunning;

    public TelegramSignalListener(
        TelegramSettings settings,
        SignalParser parser,
        ILogger? logger = null)
        : base(logger)
    {
        _settings = settings;
        _parser = parser;
        _clientLogLevel = ParseLogLevel(settings.ClientLogLevel);

        ConfigureClientLogging();
    }

    protected override async Task OnStartAsync(CancellationToken ct)
    {
        // Create WTelegram client
        _client = new Client(Config);

        // Login user
        var user = await DoLogin(_client, ct);
        _logger.Information("Logged in as {Username} (@{UserId})", user.username, user.id);

        // Subscribe to updates
        _client.OnUpdates += OnUpdate;

        // Get dialogs to verify channel access
        var dialogs = await _client.Messages_GetAllDialogs();
        _logger.Information("Found {Count} dialogs", dialogs.dialogs.Length);

        // Build channel lookup by username and ID
        var allChannels = dialogs.chats.Values.OfType<Channel>().ToList();
        _logger.Information("Available channels: {Count} total", allChannels.Count);

        var channelsByUsername = allChannels
            .Where(c => !string.IsNullOrWhiteSpace(c.username))
            .ToDictionary(c => c.username.ToLowerInvariant(), c => c);

        var channelsById = allChannels.ToDictionary(c => c.ID, c => c);

        // Resolve channel names to IDs and build monitored set
        _monitoredChannelIds = new HashSet<long>();
        _channelNames = new Dictionary<long, string>();

        // Process channel parser mappings (resolve names and IDs)
        foreach (var mapping in _settings.Parsing.ChannelParsers)
        {
            Channel? channel = null;

            // Try to resolve by ID first
            if (mapping.ChannelId != 0)
            {
                var searchId = TelegramIdHelper.ConvertToApiFormat(mapping.ChannelId);
                channelsById.TryGetValue(searchId, out channel);
            }

            // If no ID or not found, try by name
            if (channel == null && !string.IsNullOrWhiteSpace(mapping.ChannelName))
            {
                var normalizedName = TelegramIdHelper.NormalizeUsername(mapping.ChannelName).ToLowerInvariant();
                channelsByUsername.TryGetValue(normalizedName, out channel);
            }

            if (channel != null)
            {
                mapping.ResolvedChannelId = channel.ID;
                _monitoredChannelIds.Add(channel.ID);
                _channelNames[channel.ID] = channel.Title;

                var identifier = !string.IsNullOrWhiteSpace(mapping.ChannelName)
                    ? mapping.ChannelName
                    : mapping.ChannelId.ToString();

                _logger.Information(
                    "Resolved channel '{Identifier}' -> {Title} (ID: {Id}), parser: {Parser}",
                    identifier, channel.Title, channel.ID, mapping.Parser);
            }
            else
            {
                var identifier = !string.IsNullOrWhiteSpace(mapping.ChannelName)
                    ? mapping.ChannelName
                    : mapping.ChannelId.ToString();

                _logger.Warning(
                    "Channel '{Identifier}' not found in dialogs. Make sure you have access to it.",
                    identifier);
            }
        }

        // Notify SignalParser about resolved channel IDs
        _parser.UpdateChannelMappings(_settings.Parsing.ChannelParsers);

        _logger.Information("Monitoring {Count} channels total", _monitoredChannelIds.Count);
    }

    protected override Task OnStopAsync(CancellationToken ct)
    {
        if (_client != null)
        {
            _client.OnUpdates -= OnUpdate;
            _client.Dispose();
            _client = null;
        }

        return Task.CompletedTask;
    }

    protected override ValueTask OnDisposeAsync()
    {
        _messageLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private void ConfigureClientLogging()
    {
        Helpers.Log = (severity, message) =>
        {
            var level = (LogLevel)severity;

            if (level == LogLevel.None || level < _clientLogLevel)
                return;

            var serilogLevel = level switch
            {
                LogLevel.Trace => LogEventLevel.Verbose,
                LogLevel.Debug => LogEventLevel.Debug,
                LogLevel.Information => LogEventLevel.Information,
                LogLevel.Warning => LogEventLevel.Warning,
                LogLevel.Error => LogEventLevel.Error,
                LogLevel.Critical => LogEventLevel.Fatal,
                _ => LogEventLevel.Information
            };

            _logger.Write(serilogLevel, "[WTelegram] {Message}", message);
        };
    }

    private static LogLevel ParseLogLevel(string? value)
    {
        return Enum.TryParse<LogLevel>(value, true, out var level)
            ? level
            : LogLevel.Warning;
    }

    private string? Config(string what)
    {
        return what switch
        {
            "api_id" => _settings.ApiId.ToString(),
            "api_hash" => _settings.ApiHash,
            "phone_number" => _settings.PhoneNumber,
            "session_pathname" => _settings.SessionPath,
            "verification_code" => null, // Will prompt user
            "password" => null, // Will prompt user if 2FA enabled
            _ => null
        };
    }

    private async Task<User> DoLogin(Client client, CancellationToken ct)
    {
        // LoginUserIfNeeded handles phone, code, and 2FA automatically via Config callback
        var myself = await client.LoginUserIfNeeded();

        if (myself == null)
        {
            throw new InvalidOperationException("Failed to login to Telegram");
        }

        return myself;
    }

    private async Task OnUpdate(UpdatesBase updates)
    {
        try
        {
            // Process all updates
            foreach (var update in updates.UpdateList)
            {
                if (update is UpdateNewMessage { message: Message message })
                {
                    await ProcessMessage(message);
                }
                else if (update is UpdateNewChannelMessage { message: Message channelMessage })
                {
                    await ProcessMessage(channelMessage);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error processing Telegram update");
        }
    }

    private async Task ProcessMessage(MessageBase messageBase)
    {
        if (messageBase is not Message message)
            return;

        try
        {
            // Extract channel/chat ID
            var peerId = message.Peer.ID;

            // Check if this channel is in our monitored list
            if (!TelegramIdHelper.IsMonitoredChannel(peerId, _monitoredChannelIds))
            {
                _logger.Debug("Ignoring message from unmonitored channel {ChannelId}", peerId);
                return;
            }

            if (message.fwd_from != null)
            {
                _logger.Debug("Ignoring forwarded message {MessageId} from channel {ChannelId}", message.ID, peerId);
                return;
            }

            _logger.Information("Received message from monitored channel {ChannelId}", peerId);

            // Check for duplicate message
            await _messageLock.WaitAsync();
            try
            {
                if (_processedMessageIds.Contains(message.ID))
                {
                    _logger.Debug("Duplicate message {MessageId}, skipping", message.ID);
                    return;
                }

                _processedMessageIds.Add(message.ID);

                // Keep only last 1000 message IDs to prevent memory leak
                if (_processedMessageIds.Count > 1000)
                {
                    var toRemove = _processedMessageIds.Take(100).ToList();
                    foreach (var id in toRemove)
                    {
                        _processedMessageIds.Remove(id);
                    }
                }
            }
            finally
            {
                _messageLock.Release();
            }

            // Extract message text
            var messageText = message.message;
            if (string.IsNullOrWhiteSpace(messageText))
            {
                _logger.Debug("Empty message, skipping");
                return;
            }

            // Get channel name
            var channelName = GetChannelName(message.Peer);

            _logger.Debug("Processing message from {Channel}: {Preview}",
                channelName, messageText.Length > 50 ? messageText.Substring(0, 50) + "..." : messageText);

            // Parse signal
            var source = new SignalSource
            {
                ChannelName = channelName,
                ChannelId = peerId,
                MessageId = message.ID
            };

            var parseResult = _parser.Parse(messageText, source);

            if (parseResult.IsSuccess && parseResult.Signal != null)
            {
                _logger.Information("Signal received from {Channel}: {Symbol} {Direction}",
                    channelName, parseResult.Signal.Symbol, parseResult.Signal.Direction);

                // Raise event
                OnSignalReceived?.Invoke(parseResult.Signal);
            }
            else if (!string.IsNullOrEmpty(parseResult.ErrorMessage))
            {
                _logger.Debug("Failed to parse signal: {Error}", parseResult.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error processing message {MessageId}", messageBase.ID);
        }
    }

    private string GetChannelName(Peer peer)
    {
        if (_channelNames.TryGetValue(peer.ID, out var name))
            return name;

        return $"Channel_{peer.ID}";
    }
}
