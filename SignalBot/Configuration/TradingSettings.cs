namespace SignalBot.Configuration;

/// <summary>
/// Trading execution settings
/// </summary>
public class TradingSettings
{
    public string SignalSymbolSuffix { get; set; } = "USDT";
    public string DefaultSymbolSuffix { get; set; } = "USDT";
    public string MarginType { get; set; } = "Isolated";
    public string PositionMode { get; set; } = "OneWay";

    public string EntryMode { get; set; } = "Market";
    public int MaxConcurrentPositions { get; set; } = 5;
    public TimeSpan MinTimeBetweenSignals { get; set; } = TimeSpan.FromMinutes(1);

    public string TargetStrategy { get; set; } = "PartialClose";
    public List<decimal> TargetClosePercents { get; set; } = new() { 25, 25, 25, 25 };
    public bool MoveStopToBreakeven { get; set; } = true;
    public bool TrailingStopEnabled { get; set; } = false;
}
