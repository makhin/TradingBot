namespace ComplexBot.Services.Backtesting;

public record BacktestSettings
{
    public decimal InitialCapital { get; init; } = 10000m;
    public decimal CommissionPercent { get; init; } = 0.1m;  // 0.1% Binance fee
    public decimal SlippagePercent { get; init; } = 0.05m;
}
