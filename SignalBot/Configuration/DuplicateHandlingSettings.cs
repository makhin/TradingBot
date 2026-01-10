namespace SignalBot.Configuration;

/// <summary>
/// Settings for handling duplicate signals
/// </summary>
public class DuplicateHandlingSettings
{
    public string SameDirection { get; set; } = "Ignore"; // Ignore, Add, Increase
    public string OppositeDirection { get; set; } = "Ignore"; // Ignore, Close, Flip
    public int MaxPositionsPerSymbol { get; set; } = 1;
    public TimeSpan MinTimeBetweenDuplicates { get; set; } = TimeSpan.FromMinutes(5);
    public bool AllowDuplicateOnPartialClose { get; set; } = true;
}
