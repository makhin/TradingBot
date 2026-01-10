namespace SignalBot.Models;

/// <summary>
/// Source information for a trading signal
/// </summary>
public record SignalSource
{
    public required string ChannelName { get; init; }
    public required long ChannelId { get; init; }
    public required int MessageId { get; init; }
}
