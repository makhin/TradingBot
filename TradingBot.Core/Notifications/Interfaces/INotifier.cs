using System.Threading;
using System.Threading.Tasks;
using TradingBot.Core.Models;
using TradingBot.Core.Analytics;

namespace TradingBot.Core.Notifications;

public interface INotifier
{
    Task SendTradeOpen(TradeSignal signal, decimal quantity, decimal riskAmount,
        CancellationToken cancellationToken = default);
    Task SendTradeClose(string symbol, decimal entryPrice, decimal exitPrice,
        decimal pnl, decimal rMultiple, string reason,
        CancellationToken cancellationToken = default);
    Task SendDrawdownAlert(decimal currentDrawdown, decimal dailyDrawdown,
        CancellationToken cancellationToken = default);
    Task SendCircuitBreakerTriggered(string reason,
        CancellationToken cancellationToken = default);
    Task SendDailySummary(TradeJournalStats stats, decimal equity, decimal drawdown,
        CancellationToken cancellationToken = default);
    Task SendError(string errorMessage,
        CancellationToken cancellationToken = default);
    Task SendMessageAsync(string message,
        CancellationToken cancellationToken = default);
}
