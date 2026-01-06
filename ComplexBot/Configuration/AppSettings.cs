using ComplexBot.Models;

namespace ComplexBot.Configuration;

public class AppSettings
{
    public PathSettings Paths { get; set; } = new();
    public ConnectionSettings Connection { get; set; } = new();
    public List<KlineInterval> AllowedIntervals { get; set; } = new()
    {
        KlineInterval.OneHour,
        KlineInterval.FourHour,
        KlineInterval.OneDay
    };
}
