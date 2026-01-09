using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Core.Lifecycle;

public interface IGracefulShutdownHandler
{
    Task InitiateShutdownAsync(string reason, CancellationToken ct = default);
}
