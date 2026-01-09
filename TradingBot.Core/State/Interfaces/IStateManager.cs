using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Core.State;

public interface IStateManager<TState> where TState : class
{
    Task SaveStateAsync(TState state, CancellationToken ct = default);
    Task<TState?> LoadStateAsync(CancellationToken ct = default);
    Task<TState?> LoadBackupAsync(CancellationToken ct = default);
    bool StateExists();
    Task DeleteStateAsync(CancellationToken ct = default);
    Task CreateBackupAsync(CancellationToken ct = default);
}
