using SignalBot.Models;
using SignalBot.State;
using Serilog;

namespace SignalBot.Services.Statistics;

public class TradeStatisticsService : ITradeStatisticsService
{
    private readonly ITradeStatisticsStore _store;
    private readonly IReadOnlyList<TradeStatisticsWindow> _windows;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private List<TradeStatisticsEntry> _entries = new();
    private bool _isLoaded;

    public TradeStatisticsService(
        ITradeStatisticsStore store,
        IEnumerable<TradeStatisticsWindow> windows,
        ILogger? logger = null)
    {
        _store = store;
        _windows = windows.ToList();
        _logger = logger ?? Log.ForContext<TradeStatisticsService>();
    }

    public async Task RecordClosedPositionAsync(SignalPosition position, CancellationToken ct = default)
    {
        if (position.Status != PositionStatus.Closed || position.ClosedAt is null)
        {
            return;
        }

        await EnsureLoadedAsync(ct);

        await _lock.WaitAsync(ct);
        try
        {
            var entry = new TradeStatisticsEntry
            {
                PositionId = position.Id,
                Symbol = position.Symbol,
                ClosedAt = position.ClosedAt.Value,
                RealizedPnl = position.RealizedPnl
            };

            var existingIndex = _entries.FindIndex(e => e.PositionId == position.Id);
            if (existingIndex >= 0)
            {
                _entries[existingIndex] = entry;
            }
            else
            {
                _entries.Add(entry);
            }

            PruneOldEntries(DateTime.UtcNow);

            await _store.SaveAsync(new TradeStatisticsState
            {
                LastUpdated = DateTime.UtcNow,
                Entries = _entries.ToList()
            }, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<TradeStatisticsReport> GetReportAsync(DateTime? now = null, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        var timestamp = now ?? DateTime.UtcNow;
        var reports = new List<TradeStatisticsWindowReport>();

        foreach (var window in _windows)
        {
            var cutoff = timestamp - window.Duration;
            var windowEntries = _entries.Where(e => e.ClosedAt >= cutoff).ToList();

            var profit = windowEntries.Where(e => e.RealizedPnl > 0).Sum(e => e.RealizedPnl);
            var loss = windowEntries.Where(e => e.RealizedPnl < 0).Sum(e => e.RealizedPnl);
            var net = windowEntries.Sum(e => e.RealizedPnl);

            reports.Add(new TradeStatisticsWindowReport
            {
                Name = window.Name,
                Duration = window.Duration,
                TradeCount = windowEntries.Count,
                Profit = profit,
                Loss = loss,
                Net = net
            });
        }

        return new TradeStatisticsReport
        {
            GeneratedAt = timestamp,
            Windows = reports
        };
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_isLoaded)
        {
            return;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_isLoaded)
            {
                return;
            }

            var state = await _store.LoadAsync(ct);
            _entries = state.Entries.ToList();
            _isLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load trade statistics state");
            _entries = new List<TradeStatisticsEntry>();
            _isLoaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void PruneOldEntries(DateTime now)
    {
        if (_windows.Count == 0)
        {
            return;
        }

        var maxWindow = _windows.Max(w => w.Duration);
        var cutoff = now - maxWindow;
        _entries = _entries.Where(e => e.ClosedAt >= cutoff).ToList();
    }
}
