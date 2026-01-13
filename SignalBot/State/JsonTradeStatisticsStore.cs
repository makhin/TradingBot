using SignalBot.Models;
using Serilog;

namespace SignalBot.State;

/// <summary>
/// JSON file-based trade statistics store.
/// </summary>
public class JsonTradeStatisticsStore : JsonSingletonStore<TradeStatisticsState>, ITradeStatisticsStore
{
    public JsonTradeStatisticsStore(string filePath, ILogger? logger = null)
        : base(filePath, logger ?? Log.ForContext<JsonTradeStatisticsStore>())
    {
    }
}
