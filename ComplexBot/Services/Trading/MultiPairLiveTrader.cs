using ComplexBot.Models;
using ComplexBot.Services.RiskManagement;
using ComplexBot.Services.Notifications;
using ComplexBot.Configuration;

namespace ComplexBot.Services.Trading;

/// <summary>
/// Coordinates multiple symbol traders with shared capital and portfolio risk management.
/// Generic to support different trader implementations (Binance, Bybit, etc.)
/// </summary>
/// <typeparam name="TTrader">Type of symbol trader (e.g., BinanceLiveTrader)</typeparam>
public class MultiPairLiveTrader<TTrader> : IDisposable
    where TTrader : ISymbolTrader
{
    private readonly Dictionary<string, SymbolTradingUnit> _tradingUnits = new();
    private readonly SharedEquityManager _equityManager;
    private readonly PortfolioRiskManager _portfolioRiskManager;
    private readonly MultiPairLiveTradingSettings _settings;
    private readonly TelegramNotifier? _telegram;
    private readonly Func<TradingPairConfig, TTrader> _traderFactory;
    private bool _disposed;

    // Events (aggregated from all traders)
    public event Action<string, string>? OnLog;           // symbol, message
    public event Action<TradeSignal>? OnSignal;
    public event Action<Trade>? OnTrade;
    public event Action<PortfolioSnapshot>? OnPortfolioUpdate;

    public MultiPairLiveTrader(
        MultiPairLiveTradingSettings settings,
        PortfolioRiskSettings portfolioRiskSettings,
        Dictionary<string, string[]>? correlationGroups,
        Func<TradingPairConfig, TTrader> traderFactory,
        TelegramNotifier? telegram = null)
    {
        _settings = settings;
        _telegram = telegram;
        _traderFactory = traderFactory;

        _equityManager = new SharedEquityManager(settings.TotalCapital);
        _portfolioRiskManager = new PortfolioRiskManager(
            portfolioRiskSettings,
            correlationGroups
        );

        // Wire equity manager events
        _equityManager.OnTotalEquityUpdate += equity =>
        {
            OnPortfolioUpdate?.Invoke(_equityManager.GetSnapshot());
        };

        _equityManager.OnDrawdownAlert += async drawdown =>
        {
            if (_telegram != null)
                await _telegram.SendMessageAsync($"⚠️ Portfolio drawdown alert: {drawdown:F1}%");
            Log("PORTFOLIO", $"Drawdown alert: {drawdown:F1}%");
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Log("STARTUP", $"Starting multi-pair trading with {_settings.TradingPairs.Count} configurations");

        // Group pairs by symbol for multi-timeframe support
        var symbolGroups = _settings.TradingPairs
            .GroupBy(p => p.Symbol)
            .ToList();

        foreach (var symbolGroup in symbolGroups)
        {
            var symbol = symbolGroup.Key;
            var configs = symbolGroup.ToList();

            // Find primary trader config
            var primaryConfig = configs.FirstOrDefault(c => c.Role == "Primary");
            if (primaryConfig == null)
            {
                Log(symbol, "WARNING: No primary strategy found, skipping symbol");
                continue;
            }

            // Create primary trader
            var primaryTrader = _traderFactory(primaryConfig);
            var tradingUnit = new SymbolTradingUnit(symbol, primaryTrader);

            // Add filter traders for multi-timeframe
            var filterConfigs = configs.Where(c => c.Role == "Filter").ToList();
            foreach (var filterConfig in filterConfigs)
            {
                var filterTrader = _traderFactory(filterConfig);
                var filter = CreateSignalFilter(filterConfig);

                if (filter != null)
                {
                    tradingUnit.AddFilter(filterTrader, filter);
                }
            }

            // Wire trading unit events
            WireTradingUnitEvents(symbol, tradingUnit);

            _tradingUnits[symbol] = tradingUnit;

            // Calculate capital allocation
            var allocation = CalculateSymbolAllocation(primaryConfig);
            _equityManager.AllocateCapital(symbol, allocation);

            Log(symbol, $"Allocated {allocation:N2} USDT ({(allocation / _settings.TotalCapital * 100):F1}%)");
        }

        // Start all trading units in parallel
        Log("STARTUP", $"Starting {_tradingUnits.Count} trading units...");
        var tasks = _tradingUnits.Values.Select(unit => unit.StartAsync(cancellationToken));
        await Task.WhenAll(tasks);

        Log("STARTUP", "All trading units started successfully");

        // Keep running until cancelled
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Log("SHUTDOWN", "Multi-pair trading stopped by user");
        }
    }

    public async Task StopAsync()
    {
        Log("SHUTDOWN", "Stopping all trading units...");
        var tasks = _tradingUnits.Values.Select(unit => unit.StopAsync());
        await Task.WhenAll(tasks);
        Log("SHUTDOWN", "All trading units stopped");
    }

    public void AddTradingPair(TradingPairConfig config)
    {
        if (_tradingUnits.ContainsKey(config.Symbol))
        {
            throw new InvalidOperationException($"Trading unit for {config.Symbol} already exists");
        }

        // Calculate capital allocation
        var allocation = CalculateSymbolAllocation(config);
        _equityManager.AllocateCapital(config.Symbol, allocation);

        // Create trader via factory
        var trader = _traderFactory(config);
        var tradingUnit = new SymbolTradingUnit(config.Symbol, trader);

        // Wire events
        WireTradingUnitEvents(config.Symbol, tradingUnit);

        _tradingUnits[config.Symbol] = tradingUnit;

        Log(config.Symbol, $"Trading pair added with {allocation:N2} USDT allocation");
    }

    public void RemoveTradingPair(string symbol)
    {
        if (_tradingUnits.TryGetValue(symbol, out var tradingUnit))
        {
            tradingUnit.Dispose();
            _tradingUnits.Remove(symbol);
            _equityManager.ReleaseCapital(symbol);
            Log(symbol, "Trading pair removed");
        }
    }

    public IReadOnlyList<string> GetActiveSymbols() => _tradingUnits.Keys.ToList();

    public PortfolioSnapshot GetPortfolioSnapshot() => _equityManager.GetSnapshot();

    private void WireTradingUnitEvents(string symbol, SymbolTradingUnit tradingUnit)
    {
        tradingUnit.OnLog += msg => OnLog?.Invoke(symbol, msg);

        tradingUnit.OnSignal += signal =>
        {
            OnSignal?.Invoke(signal);
            // TODO: Add telegram notification for signals
        };

        tradingUnit.OnTrade += trade =>
        {
            OnTrade?.Invoke(trade);
            _equityManager.RecordTradePnL(symbol, trade.PnL ?? 0m);
            // TODO: Add telegram notification for trades
        };

        tradingUnit.OnEquityUpdate += equity =>
        {
            _equityManager.UpdateSymbolEquity(symbol, equity);
        };
    }

    private decimal CalculateSymbolAllocation(TradingPairConfig config)
    {
        // Only allocate for Primary strategies, not Filters
        if (config.Role != "Primary")
        {
            return 0m;
        }

        // Count only Primary strategies for allocation
        var primaryConfigs = _settings.TradingPairs
            .Where(p => p.Role == "Primary")
            .ToList();

        return _settings.AllocationMode switch
        {
            "Equal" => _settings.TotalCapital / primaryConfigs.Count,

            "Weighted" => _settings.TotalCapital *
                (config.WeightPercent ?? (100m / primaryConfigs.Count)) / 100m,

            "Dynamic" => _settings.TotalCapital / primaryConfigs.Count, // TODO: ATR-based allocation

            _ => _settings.TotalCapital / primaryConfigs.Count
        };
    }

    private ISignalFilter? CreateSignalFilter(TradingPairConfig config)
    {
        if (config.Role != "Filter" || string.IsNullOrEmpty(config.FilterMode))
        {
            return null;
        }

        var filterMode = config.FilterMode.ToLowerInvariant() switch
        {
            "confirm" => FilterMode.Confirm,
            "veto" => FilterMode.Veto,
            "score" => FilterMode.Score,
            _ => FilterMode.Confirm
        };

        // Create filter based on strategy type
        return config.Strategy.ToUpperInvariant() switch
        {
            "RSI" => new SignalFilters.RsiSignalFilter(
                overboughtThreshold: 70m,
                oversoldThreshold: 30m,
                mode: filterMode
            ),

            "ADX" => new SignalFilters.AdxSignalFilter(
                minTrendStrength: 20m,
                strongTrendThreshold: 30m,
                mode: filterMode
            ),

            "TRENDALIGNMENT" => new SignalFilters.TrendAlignmentFilter(
                mode: filterMode,
                requireStrictAlignment: true
            ),

            _ => null
        };
    }

    private void Log(string context, string message)
    {
        var formatted = $"[{DateTime.UtcNow:HH:mm:ss}] [MultiPair:{context}] {message}";
        OnLog?.Invoke(context, formatted);
        Console.WriteLine(formatted);
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var tradingUnit in _tradingUnits.Values)
        {
            tradingUnit.Dispose();
        }
        _tradingUnits.Clear();

        _disposed = true;
    }
}
