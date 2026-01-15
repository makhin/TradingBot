namespace SignalBot.Configuration;

/// <summary>
/// Configuration for trade statistics aggregation.
/// </summary>
public class TradeStatisticsSettings
{
    public List<TradeStatisticsWindowSettings> Windows { get; set; } = new();
}

public class TradeStatisticsWindowSettings
{
    public string Name { get; set; } = "24h";
    public TimeSpan Duration { get; set; } = TimeSpan.FromHours(24);
}
