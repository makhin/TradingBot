namespace SignalBot.Configuration;

/// <summary>
/// State persistence settings
/// </summary>
public class StateSettings
{
    public string StatePath { get; set; } = "signalbot_state.json";
    public string StatisticsPath { get; set; } = "signalbot_statistics.json";
    public bool BackupEnabled { get; set; } = true;
    public int AutoSaveIntervalSeconds { get; set; } = 30;
}
