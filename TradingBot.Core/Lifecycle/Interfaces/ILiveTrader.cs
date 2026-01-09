using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Core.State;

namespace TradingBot.Core.Lifecycle;

/// <summary>
/// Interface for live trading bots to enable graceful shutdown
/// </summary>
public interface ILiveTrader
{
    Task<BotState> BuildCurrentState();
    Task<List<SavedPosition>> GetOpenPositions();
    Task CancelOcoOrdersForSymbol(string symbol);
    Task ClosePosition(string symbol, string reason);
    Task StopAsync(CancellationToken cancellationToken = default);
}
