using ComplexBot.Services.Backtesting;

namespace ComplexBot.Configuration.Optimization;

public class BacktestingSettings
{
    public decimal InitialCapital { get; set; } = 10000m;
    public decimal CommissionPercent { get; set; } = 0.1m;
    public decimal SlippagePercent { get; set; } = 0.05m;

    public BacktestSettings ToBacktestSettings() => new()
    {
        InitialCapital = InitialCapital,
        CommissionPercent = CommissionPercent,
        SlippagePercent = SlippagePercent
    };
}
