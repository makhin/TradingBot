using System.Linq;
using System.Reflection;
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

    // Polling mechanism for broadcast channels
    private Dictionary<long, int> _lastMessageIds = new();
    private Dictionary<long, InputPeer> _channelInputPeers = new();
    private Timer? _pollingTimer;
    private const int PollingIntervalSeconds = 30;

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

        // LOG ALL AVAILABLE CHANNELS
        _logger.Information("=== ALL AVAILABLE CHANNELS ===");
        foreach (var ch in allChannels)
        {
            _logger.Information("  Channel: '{Title}' | ID: {Id} | Username: @{Username}",
                ch.Title,
                ch.ID,
                ch.username ?? "<none>");
        }
        _logger.Information("=== END OF CHANNEL LIST ===");

        var channelsByUsername = allChannels
            .Where(c => !string.IsNullOrWhiteSpace(c.username))
            .ToDictionary(c => c.username.ToLowerInvariant(), c => c);

        var channelsById = allChannels.ToDictionary(c => c.ID, c => c);

        // Resolve channel names to IDs and build monitored set
        _monitoredChannelIds = new HashSet<long>();
        _channelNames = new Dictionary<long, string>();

        foreach (var chat in dialogs.chats.Values)
        {
            TrackPeerName(chat);
        }

        // LOG CONFIGURED CHANNELS TO MONITOR
        _logger.Information("=== CHANNELS CONFIGURED IN SETTINGS ===");
        foreach (var mapping in _settings.Parsing.ChannelParsers)
        {
            _logger.Information("  Config entry: Name='{Name}' | ID={Id} | Parser={Parser}",
                mapping.ChannelName ?? "<null>",
                mapping.ChannelId,
                mapping.Parser);
        }
        _logger.Information("=== END OF CONFIG ===");

        // Process channel parser mappings (resolve names and IDs)
        _logger.Information("=== RESOLVING CHANNELS ===");
        foreach (var mapping in _settings.Parsing.ChannelParsers)
        {
            Channel? channel = null;

            var identifier = !string.IsNullOrWhiteSpace(mapping.ChannelName)
                ? mapping.ChannelName
                : mapping.ChannelId.ToString();

            _logger.Information("  Resolving '{Identifier}'...", identifier);

            // Try to resolve by ID first
            if (mapping.ChannelId != 0)
            {
                var searchId = TelegramIdHelper.ConvertToApiFormat(mapping.ChannelId);
                _logger.Information("    Trying by ID: {OriginalId} -> API format: {SearchId}",
                    mapping.ChannelId, searchId);

                if (channelsById.TryGetValue(searchId, out channel))
                {
                    _logger.Information("    ✅ Found by ID: '{Title}'", channel.Title);
                }
                else
                {
                    _logger.Information("    ❌ Not found by ID {SearchId}", searchId);
                }
            }

            // If no ID or not found, try by name
            if (channel == null && !string.IsNullOrWhiteSpace(mapping.ChannelName))
            {
                var normalizedName = TelegramIdHelper.NormalizeUsername(mapping.ChannelName).ToLowerInvariant();
                _logger.Information("    Trying by name: '{OriginalName}' -> normalized: '{NormalizedName}'",
                    mapping.ChannelName, normalizedName);

                if (channelsByUsername.TryGetValue(normalizedName, out channel))
                {
                    _logger.Information("    ✅ Found by name: '{Title}' (ID: {Id})",
                        channel.Title, channel.ID);
                }
                else
                {
                    _logger.Information("    ❌ Not found by name '{NormalizedName}'", normalizedName);
                }
            }

            if (channel != null)
            {
                mapping.ResolvedChannelId = channel.ID;
                _monitoredChannelIds.Add(channel.ID);
                _channelNames[channel.ID] = channel.Title;

                _logger.Information(
                    "  ✅ SUCCESS: '{Identifier}' -> {Title} (ID: {Id}), parser: {Parser}",
                    identifier, channel.Title, channel.ID, mapping.Parser);
            }
            else
            {
                _logger.Warning(
                    "  ❌ FAILED: Channel '{Identifier}' not found in dialogs. Make sure you have access to it.",
                    identifier);
            }
        }
        _logger.Information("=== END OF RESOLUTION ===");

        // Notify SignalParser about resolved channel IDs
        _parser.UpdateChannelMappings(_settings.Parsing.ChannelParsers);

        _logger.Information("=== FINAL MONITORED CHANNEL IDs ===");
        foreach (var id in _monitoredChannelIds)
        {
            var name = _channelNames.TryGetValue(id, out var n) ? n : "Unknown";
            _logger.Information("  Monitoring ID {Id}: {Name}", id, name);
        }
        _logger.Information("=== Total: {Count} channels ===", _monitoredChannelIds.Count);

        // Initialize polling mechanism for monitored channels
        await InitializePollingAsync(channelsById, ct);
    }

    protected override Task OnStopAsync(CancellationToken ct)
    {
        // Stop polling timer
        _pollingTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _pollingTimer?.Dispose();
        _pollingTimer = null;

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
        _pollingTimer?.Dispose();
        _messageLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task InitializePollingAsync(Dictionary<long, Channel> channelsById, CancellationToken ct)
    {
        _logger.Information("=== INITIALIZING POLLING MECHANISM ===");

        foreach (var channelId in _monitoredChannelIds)
        {
            if (!channelsById.TryGetValue(channelId, out var channel))
            {
                _logger.Warning("Cannot initialize polling for channel {ChannelId}: not found", channelId);
                continue;
            }

            try
            {
                // Create InputPeer for this channel
                var inputPeer = channel;
                _channelInputPeers[channelId] = inputPeer;

                // Get latest message to initialize last message ID
                var history = await _client!.Messages_GetHistory(inputPeer, limit: 1);

                if (history.Messages.Length > 0 && history.Messages[0] is Message lastMsg)
                {
                    _lastMessageIds[channelId] = lastMsg.ID;
                    _logger.Information("  Channel {Name} (ID: {Id}): last message ID = {MessageId}",
                        _channelNames[channelId], channelId, lastMsg.ID);
                }
                else
                {
                    _lastMessageIds[channelId] = 0;
                    _logger.Information("  Channel {Name} (ID: {Id}): no messages yet",
                        _channelNames[channelId], channelId);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize polling for channel {Name} (ID: {Id})",
                    _channelNames.GetValueOrDefault(channelId, "Unknown"), channelId);
            }
        }

        // Start polling timer
        _logger.Information("Starting polling timer (interval: {Seconds} seconds)", PollingIntervalSeconds);
        _pollingTimer = new Timer(
            _ => _ = PollChannelsAsync(),
            null,
            TimeSpan.FromSeconds(PollingIntervalSeconds),
            TimeSpan.FromSeconds(PollingIntervalSeconds));

        _logger.Information("=== POLLING INITIALIZED ===");
    }

    private async Task PollChannelsAsync()
    {
        if (_client == null)
            return;

        foreach (var channelId in _monitoredChannelIds.ToList())
        {
            try
            {
                if (!_channelInputPeers.TryGetValue(channelId, out var inputPeer))
                    continue;

                // Get messages since last message ID
                var lastMessageId = _lastMessageIds.GetValueOrDefault(channelId, 0);
                var history = await _client.Messages_GetHistory(inputPeer, limit: 20);

                // Process new messages in chronological order (oldest first)
                var newMessages = history.Messages
                    .OfType<Message>()
                    .Where(m => m.ID > lastMessageId)
                    .OrderBy(m => m.ID)
                    .ToList();

                if (newMessages.Any())
                {
                    _logger.Information("📬 Polling found {Count} new message(s) in {Channel}",
                        newMessages.Count, _channelNames.GetValueOrDefault(channelId, "Unknown"));

                    foreach (var message in newMessages)
                    {
                        _logger.Information("  📝 NewChannelMessage from {ChannelName} ({ChannelId}): {Preview}",
                            _channelNames.GetValueOrDefault(channelId, "Unknown"),
                            channelId,
                            BuildPreview(message.message ?? "", 100));

                        await ProcessMessage(message);

                        // Update last message ID
                        _lastMessageIds[channelId] = message.ID;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error polling channel {Name} (ID: {Id})",
                    _channelNames.GetValueOrDefault(channelId, "Unknown"), channelId);
            }
        }
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
            TrackPeerNamesFromUpdates(updates);

            _logger.Information("📨 Telegram update: {UpdateType}, {Count} item(s)",
                updates.GetType().Name,
                updates.UpdateList.Count());

            // Process all updates
            foreach (var update in updates.UpdateList)
            {
                Message? msg = null;
                string updateTypeDesc = update.GetType().Name;

                // Extract message from different update types
                if (update is UpdateNewMessage { message: Message message })
                {
                    msg = message;
                    updateTypeDesc = "NewMessage";
                }
                else if (update is UpdateNewChannelMessage { message: Message channelMessage })
                {
                    msg = channelMessage;
                    updateTypeDesc = "NewChannelMessage";
                }
                else if (update is UpdateEditChannelMessage { message: Message editedChannelMessage })
                {
                    msg = editedChannelMessage;
                    updateTypeDesc = "EditChannelMessage";
                }
                else if (update is UpdateEditMessage { message: Message editedMessage })
                {
                    msg = editedMessage;
                    updateTypeDesc = "EditMessage";
                }

                // Log with channel and text preview
                if (msg != null)
                {
                    var peerId = GetPeerId(msg.Peer);
                    var channelName = GetChannelName(msg.Peer);
                    var textPreview = BuildPreview(msg.message ?? "", 100);

                    _logger.Information("  📝 {UpdateType} from {ChannelName} ({PeerId}): {Preview}",
                        updateTypeDesc, channelName, peerId, textPreview);

                    await ProcessMessage(msg);
                }
                else
                {
                    _logger.Information("  ℹ️  Update item: {UpdateType}", update.GetType().Name);
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
            // Extract channel/chat ID - use correct ID from typed Peer
            var peerId = GetPeerId(message.Peer);
            var channelName = GetChannelName(message.Peer);
            var messageText = message.message;

            // Check if this channel is in our monitored list
            var isMonitored = TelegramIdHelper.IsMonitoredChannel(peerId, _monitoredChannelIds);
            _logger.Information("    🔍 Checking channel {ChannelName} (ID={PeerId}): isMonitored={IsMonitored}",
                channelName, peerId, isMonitored);

            if (!isMonitored)
            {
                _logger.Information("    ⏭️  Skip: not monitored (ID {PeerId} not in monitored list)", peerId);
                return;
            }

            if (message.fwd_from != null)
            {
                _logger.Information("    ⏭️  Skip: forwarded");
                return;
            }

            // Check for duplicate message
            await _messageLock.WaitAsync();
            try
            {
                if (_processedMessageIds.Contains(message.ID))
                {
                    _logger.Information("    ⏭️  Skip: duplicate");
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

            if (string.IsNullOrWhiteSpace(messageText))
            {
                _logger.Information("    ⏭️  Skip: empty (media={HasMedia})", message.media != null);
                return;
            }

            _logger.Information("    📩 FULL TEXT:\n{FullText}",
                messageText);

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
                _logger.Information("    ✅ SIGNAL: {Symbol} {Direction} @ {Entry}",
                    parseResult.Signal.Symbol, parseResult.Signal.Direction, parseResult.Signal.Entry);

                // Raise event
                if (OnSignalReceived == null)
                {
                    _logger.Warning(
                        "Signal parsed but no subscribers attached. Signal from {Channel} ({ChannelId}) ignored.",
                        channelName,
                        peerId);
                    return;
                }

                OnSignalReceived.Invoke(parseResult.Signal);
            }
            else if (!string.IsNullOrEmpty(parseResult.ErrorMessage))
            {
                _logger.Information("    ⚠️  Parse error: {Error}",
                    parseResult.ErrorMessage);
            }
            else
            {
                _logger.Information("    ℹ️  Not a signal");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error processing message {MessageId}", messageBase.ID);
        }
    }

    private long GetPeerId(Peer peer)
    {
        // Extract the correct ID based on Peer type
        // WTelegram Peer types have specific ID properties
        return peer switch
        {
            PeerChannel peerChannel => peerChannel.channel_id,
            PeerChat peerChat => peerChat.chat_id,
            PeerUser peerUser => peerUser.user_id,
            _ => peer.ID // Fallback to generic ID
        };
    }

    private string GetChannelName(Peer peer)
    {
        var peerId = GetPeerId(peer);
        if (_channelNames.TryGetValue(peerId, out var name))
            return name;

        return $"Channel_{peerId}";
    }

    private void TrackPeerNamesFromUpdates(UpdatesBase updates)
    {
        var chatsProperty = updates.GetType().GetProperty("chats", BindingFlags.Public | BindingFlags.Instance)
            ?? updates.GetType().GetProperty("Chats", BindingFlags.Public | BindingFlags.Instance);
        if (chatsProperty == null)
        {
            return;
        }

        var chatsValue = chatsProperty.GetValue(updates);
        switch (chatsValue)
        {
            case System.Collections.IDictionary dictionary:
                foreach (var value in dictionary.Values)
                {
                    if (value != null)
                    {
                        TrackPeerName(value);
                    }
                }

                break;
            case System.Collections.IEnumerable enumerable:
                foreach (var value in enumerable)
                {
                    if (value != null)
                    {
                        TrackPeerName(value);
                    }
                }

                break;
        }
    }

    private void TrackPeerName(object chat)
    {
        var peerId = TryGetLongProperty(chat, "ID", "id");
        if (!peerId.HasValue)
        {
            return;
        }

        var title = TryGetStringProperty(chat, "Title", "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        // Log if this is a new peer we're seeing
        if (!_channelNames.ContainsKey(peerId.Value))
        {
            _logger.Information("    📋 Tracking new peer: ID={Id}, Title='{Title}'", peerId.Value, title);
        }

        _channelNames[peerId.Value] = title;
    }

    private static long? TryGetLongProperty(object target, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property?.PropertyType == typeof(long) && property.GetValue(target) is long value)
            {
                return value;
            }
        }

        return null;
    }

    private static string? TryGetStringProperty(object target, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property?.PropertyType == typeof(string) && property.GetValue(target) is string value)
            {
                return value;
            }
        }

        return null;
    }

    private static string BuildPreview(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "<empty>";
        }

        var normalized = text.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized.Substring(0, maxLength)}...";
    }
}
