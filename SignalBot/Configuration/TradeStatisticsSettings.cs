namespace SignalBot.Configuration;

/// <summary>
/// Configuration for trade statistics aggregation.
/// </summary>
public class TradeStatisticsSettings
{
    public List<TradeStatisticsWindowSettings> Windows { get; set; } = new()
    {
        new TradeStatisticsWindowSettings
        {
            Name = "24h",
            Duration = TimeSpan.FromHours(24)
        },
        new TradeStatisticsWindowSettings
        {
            Name = "7d",
            Duration = TimeSpan.FromDays(7)
        },
        new TradeStatisticsWindowSettings
        {
            Name = "30d",
            Duration = TimeSpan.FromDays(30)
        }
    };
}

public class TradeStatisticsWindowSettings
{
    public string Name { get; set; } = "24h";
    public TimeSpan Duration { get; set; } = TimeSpan.FromHours(24);
}
