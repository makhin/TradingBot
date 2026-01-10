namespace SignalBot.Configuration;

/// <summary>
/// Emergency stop settings
/// </summary>
public class EmergencySettings
{
    public decimal MaxDailyLossPercent { get; set; } = 5.0m;
    public decimal MaxSessionLossPercent { get; set; } = 10.0m;
    public string MaxLossAction { get; set; } = "StopNewTrades"; // StopNewTrades, CloseAll, Alert
    public bool CloseAllOnEmergencyStop { get; set; } = true;
}
