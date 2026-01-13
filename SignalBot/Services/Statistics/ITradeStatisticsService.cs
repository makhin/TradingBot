using SignalBot.Models;

namespace SignalBot.Services.Statistics;

public interface ITradeStatisticsService
{
    Task RecordClosedPositionAsync(SignalPosition position, CancellationToken ct = default);
    Task<TradeStatisticsReport> GetReportAsync(DateTime? now = null, CancellationToken ct = default);
}
