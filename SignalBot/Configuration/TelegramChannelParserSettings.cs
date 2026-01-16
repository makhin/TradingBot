namespace SignalBot.Configuration;

/// <summary>
/// Per-channel parser mapping. Can specify either ChannelId or ChannelName (or both).
/// </summary>
public class TelegramChannelParserSettings
{
    /// <summary>
    /// Telegram channel ID (e.g., -1003045070745 or 3045070745).
    /// Takes precedence over ChannelName if both are specified.
    /// </summary>
    public long ChannelId { get; set; }

    /// <summary>
    /// Telegram channel username (e.g., "Fat_Pig_Signals1").
    /// Will be resolved to ChannelId at startup.
    /// </summary>
    public string? ChannelName { get; set; }

    /// <summary>
    /// Parser name to use for this channel.
    /// </summary>
    public string Parser { get; set; } = string.Empty;

    /// <summary>
    /// Resolved channel ID (set at runtime after resolving ChannelName).
    /// </summary>
    internal long ResolvedChannelId { get; set; }

    /// <summary>
    /// Gets the effective channel ID (ResolvedChannelId if set, otherwise ChannelId).
    /// </summary>
    public long GetEffectiveChannelId() => ResolvedChannelId != 0 ? ResolvedChannelId : ChannelId;

    /// <summary>
    /// Returns true if this mapping has a valid channel identifier (ID or name).
    /// </summary>
    public bool HasChannelIdentifier() => ChannelId != 0 || !string.IsNullOrWhiteSpace(ChannelName);
}
