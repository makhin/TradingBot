using BacktestEngineSettings = ComplexBot.Services.Backtesting.BacktestSettings;

namespace ComplexBot.Configuration;

public class BacktestSettings
{
    public decimal InitialCapital { get; set; } = 10000m;
    public decimal CommissionPercent { get; set; } = 0.1m;
    public decimal SlippagePercent { get; set; } = 0.05m;

    public BacktestEngineSettings ToBacktestSettings() => new()
    {
        InitialCapital = InitialCapital,
        CommissionPercent = CommissionPercent,
        SlippagePercent = SlippagePercent
    };
}
