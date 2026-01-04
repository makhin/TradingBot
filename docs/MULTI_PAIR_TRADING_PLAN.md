# Multi-Pair Trading Implementation Plan

## Overview

Implementation of multi-pair trading with shared capital pool in a single application instance.

**Architecture Decision:** Shared capital pool - all trading pairs share total equity for more efficient capital utilization.

**Design Principles:**
- Use interfaces and base classes to reduce code duplication
- Leverage generics for type-safe configuration
- Fix existing dead code (CorrelationGroups not connected to live trading)

---

## Current State Analysis

### Already Implemented (Ready to Use)
| Component | Status | Location |
|-----------|--------|----------|
| `PortfolioRiskManager` | Complete | `Services/RiskManagement/PortfolioRiskManager.cs` |
| `AggregatedEquityTracker` | Complete | `Services/RiskManagement/AggregatedEquityTracker.cs` |
| `StateManager` (multi-position) | Complete | `Services/State/StateManager.cs` |
| Correlation groups config | Complete | `appsettings.json` -> `PortfolioRisk` |
| `StrategyBase<TSettings>` | Complete | `Services/Strategies/StrategyBase.cs` |
| All models have Symbol field | Complete | `Models/` |

### Dead Code Issue: CorrelationGroups

**Problem:** `CorrelationGroups` are configured but NOT used in live trading!

```csharp
// BinanceLiveTrader.cs:51 - creates local RiskManager only
_riskManager = new RiskManager(riskSettings, _settings.InitialCapital);
// PortfolioRiskManager is NEVER created or used!
```

**Fix:** Connect `PortfolioRiskManager` to `BinanceLiveTrader` in multi-pair mode.

### Needs Implementation
| Component | Effort | Priority |
|-----------|--------|----------|
| `ISymbolTrader` interface | ~30 LOC | P0 |
| `SymbolTraderBase<T>` base class | ~150 LOC | P0 |
| `MultiPairTradingSettings` | ~50 LOC | P0 |
| `MultiPairLiveTrader<T>` | ~250 LOC | P0 |
| `SharedEquityManager` | ~100 LOC | P0 |
| Refactor `BinanceLiveTrader` | ~50 LOC changes | P1 |
| `LiveTradingRunner` updates | ~150 LOC | P1 |
| Unit tests | ~200 LOC | P2 |

**Total: ~600-800 LOC**

---

## Phase 0: Abstractions and Interfaces (NEW)

### 0.1 Create `ISymbolTrader` Interface

Location: `ComplexBot/Services/Trading/ISymbolTrader.cs`

Purpose: Abstract interface for any symbol trader, enabling future exchange support.

```csharp
namespace ComplexBot.Services.Trading;

/// <summary>
/// Interface for trading a single symbol. Enables multi-exchange support.
/// </summary>
public interface ISymbolTrader : IDisposable
{
    // Identity
    string Symbol { get; }
    string Exchange { get; }  // "Binance", "Bybit", etc.

    // State
    decimal CurrentPosition { get; }
    decimal? EntryPrice { get; }
    decimal CurrentEquity { get; }
    bool IsRunning { get; }

    // Lifecycle
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();

    // Position management
    Task ClosePositionAsync(string reason);

    // Events
    event Action<string>? OnLog;
    event Action<TradeSignal>? OnSignal;
    event Action<Trade>? OnTrade;
    event Action<decimal>? OnEquityUpdate;
}
```

### 0.2 Create `SymbolTraderBase<TSettings>` Base Class

Location: `ComplexBot/Services/Trading/SymbolTraderBase.cs`

Purpose: Extract common logic from `BinanceLiveTrader` to reduce duplication.

```csharp
namespace ComplexBot.Services.Trading;

/// <summary>
/// Base class for symbol traders with common position tracking and risk management.
/// </summary>
public abstract class SymbolTraderBase<TSettings> : ISymbolTrader
    where TSettings : class, new()
{
    // Configuration
    protected readonly TSettings Settings;
    protected readonly IStrategy Strategy;
    protected readonly RiskManager RiskManager;
    protected readonly TelegramNotifier? Telegram;

    // Portfolio integration (optional)
    protected readonly PortfolioRiskManager? PortfolioRiskManager;
    protected readonly SharedEquityManager? SharedEquityManager;

    // Position state
    protected decimal _currentPosition;
    protected decimal? _entryPrice;
    protected decimal? _stopLoss;
    protected decimal? _takeProfit;
    protected bool _isRunning;

    // Buffer for candles
    protected readonly List<Candle> CandleBuffer = new();

    // Events
    public event Action<string>? OnLog;
    public event Action<TradeSignal>? OnSignal;
    public event Action<Trade>? OnTrade;
    public event Action<decimal>? OnEquityUpdate;

    // Properties
    public abstract string Symbol { get; }
    public abstract string Exchange { get; }
    public decimal CurrentPosition => _currentPosition;
    public decimal? EntryPrice => _entryPrice;
    public abstract decimal CurrentEquity { get; }
    public bool IsRunning => _isRunning;

    protected SymbolTraderBase(
        IStrategy strategy,
        RiskSettings riskSettings,
        TSettings settings,
        TelegramNotifier? telegram = null,
        PortfolioRiskManager? portfolioRiskManager = null,
        SharedEquityManager? sharedEquityManager = null)
    {
        Strategy = strategy;
        Settings = settings;
        Telegram = telegram;
        PortfolioRiskManager = portfolioRiskManager;
        SharedEquityManager = sharedEquityManager;
        RiskManager = new RiskManager(riskSettings, GetInitialCapital());
    }

    // Abstract methods - exchange-specific
    protected abstract decimal GetInitialCapital();
    protected abstract Task<decimal> GetCurrentPriceAsync();
    protected abstract Task<decimal> GetAccountBalanceAsync(string asset = "USDT");
    protected abstract Task SubscribeToKlineUpdatesAsync(CancellationToken ct);
    protected abstract Task<OrderResult> ExecuteOrderAsync(OrderSide side, decimal quantity, decimal price);
    protected abstract Task WarmupIndicatorsAsync();

    // Template method for lifecycle
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _isRunning = true;
        Log($"Starting trader on {Symbol}");

        var balance = await GetAccountBalanceAsync();
        UpdateEquity(balance);

        await WarmupIndicatorsAsync();
        await SubscribeToKlineUpdatesAsync(cancellationToken);
    }

    public abstract Task StopAsync();
    public abstract Task ClosePositionAsync(string reason);
    public abstract void Dispose();

    // Shared logic: Process candle
    protected async Task ProcessCandleAsync(Candle candle)
    {
        if (!_isRunning) return;

        CandleBuffer.Add(candle);
        TrimCandleBuffer();

        if (_currentPosition != 0)
        {
            RiskManager.UpdatePositionPrice(Symbol, candle.Close);
        }

        var signal = Strategy.Analyze(candle, _currentPosition, Symbol);

        if (signal != null)
        {
            Log($"Signal: {signal.Type} at {signal.Price:F2} - {signal.Reason}");
            OnSignal?.Invoke(signal);
            await ProcessSignalAsync(signal, candle);
        }
    }

    // Shared logic: Portfolio risk check
    protected bool CanOpenPositionWithPortfolioCheck()
    {
        // Symbol-level check
        if (!RiskManager.CanOpenPosition())
        {
            Log("Symbol risk limit reached");
            return false;
        }

        // Portfolio-level check (if connected)
        if (PortfolioRiskManager != null && !PortfolioRiskManager.CanOpenPosition(Symbol))
        {
            Log($"Portfolio risk limit reached for {Symbol}");
            return false;
        }

        return true;
    }

    // Shared logic: Update equity
    protected void UpdateEquity(decimal equity)
    {
        RiskManager.UpdateEquity(equity);
        SharedEquityManager?.UpdateSymbolEquity(Symbol, equity);
        OnEquityUpdate?.Invoke(equity);
    }

    // Shared logic: Logging
    protected void Log(string message)
    {
        var formatted = $"[{DateTime.UtcNow:HH:mm:ss}] [{Symbol}] {message}";
        OnLog?.Invoke(formatted);
        Console.WriteLine(formatted);
    }

    // Abstract: Signal processing (exchange-specific order logic)
    protected abstract Task ProcessSignalAsync(TradeSignal signal, Candle candle);

    // Helper
    protected virtual void TrimCandleBuffer(int maxSize = 200)
    {
        while (CandleBuffer.Count > maxSize)
            CandleBuffer.RemoveAt(0);
    }
}

// Shared types
public record OrderResult(bool Success, decimal FilledQuantity, decimal AveragePrice, string? ErrorMessage);
```

### 0.3 Refactor `BinanceLiveTrader` to Inherit from Base

```csharp
namespace ComplexBot.Services.Trading;

/// <summary>
/// Binance-specific implementation of symbol trader.
/// </summary>
public class BinanceLiveTrader : SymbolTraderBase<LiveTraderSettings>
{
    private readonly BinanceRestClient _restClient;
    private readonly BinanceSocketClient _socketClient;
    private readonly ExecutionValidator _executionValidator;
    private UpdateSubscription? _subscription;
    private long? _currentOcoOrderListId;
    private decimal _paperEquity;

    // Interface implementation
    public override string Symbol => Settings.Symbol;
    public override string Exchange => "Binance";
    public override decimal CurrentEquity => Settings.PaperTrade ? _paperEquity : GetAccountBalanceAsync().Result;

    public BinanceLiveTrader(
        string apiKey,
        string apiSecret,
        IStrategy strategy,
        RiskSettings riskSettings,
        LiveTraderSettings? settings = null,
        TelegramNotifier? telegram = null,
        PortfolioRiskManager? portfolioRiskManager = null,
        SharedEquityManager? sharedEquityManager = null)
        : base(strategy, riskSettings, settings ?? new(), telegram, portfolioRiskManager, sharedEquityManager)
    {
        _executionValidator = new ExecutionValidator(maxSlippagePercent: 0.5m);
        _paperEquity = Settings.InitialCapital;

        // Initialize Binance clients
        _restClient = new BinanceRestClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
            if (Settings.UseTestnet)
                options.Environment = Binance.Net.BinanceEnvironment.Testnet;
        });

        _socketClient = new BinanceSocketClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
            if (Settings.UseTestnet)
                options.Environment = Binance.Net.BinanceEnvironment.Testnet;
        });
    }

    protected override decimal GetInitialCapital() => Settings.InitialCapital;

    protected override async Task<decimal> GetCurrentPriceAsync()
    {
        var result = await _restClient.SpotApi.ExchangeData.GetPriceAsync(Settings.Symbol);
        if (!result.Success)
            throw new Exception($"Failed to get price: {result.Error?.Message}");
        return result.Data.Price;
    }

    protected override async Task<decimal> GetAccountBalanceAsync(string asset = "USDT")
    {
        var result = await _restClient.SpotApi.Account.GetAccountInfoAsync();
        if (!result.Success)
            throw new Exception($"Failed to get balance: {result.Error?.Message}");
        var balance = result.Data.Balances.FirstOrDefault(b => b.Asset == asset);
        return balance?.Available ?? 0;
    }

    protected override async Task SubscribeToKlineUpdatesAsync(CancellationToken ct)
    {
        var subscribeResult = await _socketClient.SpotApi.ExchangeData.SubscribeToKlineUpdatesAsync(
            Settings.Symbol,
            Settings.Interval,
            async data => await OnKlineUpdateAsync(data.Data)
        );

        if (!subscribeResult.Success)
            throw new Exception($"Failed to subscribe: {subscribeResult.Error?.Message}");

        _subscription = subscribeResult.Data;
        Log("Subscribed to kline updates");
    }

    private async Task OnKlineUpdateAsync(IBinanceStreamKlineData data)
    {
        var kline = data.Data;
        if (!kline.Final) return;

        var candle = new Candle(
            kline.OpenTime, kline.OpenPrice, kline.HighPrice,
            kline.LowPrice, kline.ClosePrice, kline.Volume, kline.CloseTime
        );

        await ProcessCandleAsync(candle);
    }

    protected override async Task ProcessSignalAsync(TradeSignal signal, Candle candle)
    {
        switch (signal.Type)
        {
            case SignalType.Buy when _currentPosition <= 0:
                // Check portfolio risk first
                if (!CanOpenPositionWithPortfolioCheck())
                    return;

                // ... existing buy logic ...
                break;

            case SignalType.Sell when _currentPosition >= 0:
                if (!CanOpenPositionWithPortfolioCheck())
                    return;

                // ... existing sell logic ...
                break;

            case SignalType.Exit when _currentPosition != 0:
                await ClosePositionAsync(signal.Reason ?? "Exit signal");
                break;
        }
    }

    // ... rest of Binance-specific methods (OCO orders, etc.)
}
```

### Benefits of This Refactoring

1. **Code Reuse:** ~300 lines of common logic in base class
2. **Type Safety:** Generics for settings (`LiveTraderSettings`, future `BybitTraderSettings`)
3. **Future Exchange Support:** Just implement `SymbolTraderBase<BybitSettings>` for Bybit
4. **Testability:** Can mock `ISymbolTrader` for unit tests
5. **Fix Dead Code:** Portfolio risk checks now integrated

---

## Phase 1: Configuration Structure

### 1.1 Add `MultiPairTradingSettings` to `BotConfiguration.cs`

```csharp
public class MultiPairLiveTradingSettings
{
    public bool Enabled { get; set; } = false;
    public decimal TotalCapital { get; set; } = 10000m;
    public List<TradingPairConfig> TradingPairs { get; set; } = new();
    public CapitalAllocationMode AllocationMode { get; set; } = CapitalAllocationMode.Equal;
    public bool UsePortfolioRiskManager { get; set; } = true;
}

public class TradingPairConfig
{
    public string Symbol { get; set; } = "";
    public string Interval { get; set; } = "FourHour";
    public string Strategy { get; set; } = "ADX";  // ADX, RSI, MA, Ensemble
    public decimal? WeightPercent { get; set; }    // Optional: manual weight (for Weighted mode)

    // Optional per-pair strategy overrides
    public StrategyConfigSettings? StrategyOverrides { get; set; }
}

public enum CapitalAllocationMode
{
    Equal,      // Split equally among pairs
    Weighted,   // Use WeightPercent from each pair
    Dynamic     // Adjust based on volatility/ATR (future)
}
```

### 1.2 Add to `appsettings.json`

```json
"MultiPairLiveTrading": {
  "Enabled": false,
  "TotalCapital": 10000.0,
  "AllocationMode": "Equal",
  "UsePortfolioRiskManager": true,
  "TradingPairs": [
    {
      "Symbol": "BTCUSDT",
      "Interval": "FourHour",
      "Strategy": "ADX",
      "WeightPercent": null
    },
    {
      "Symbol": "ETHUSDT",
      "Interval": "FourHour",
      "Strategy": "ADX",
      "WeightPercent": null
    },
    {
      "Symbol": "SOLUSDT",
      "Interval": "FourHour",
      "Strategy": "RSI",
      "WeightPercent": null
    }
  ]
}
```

### 1.3 Files to Modify
- `ComplexBot/Configuration/BotConfiguration.cs` - Add new settings classes
- `ComplexBot/appsettings.json` - Add configuration section

---

## Phase 2: Shared Equity Manager

### 2.1 Create `SharedEquityManager.cs`

Location: `ComplexBot/Services/Trading/SharedEquityManager.cs`

Purpose: Centralized equity tracking and allocation for all trading pairs.

```csharp
namespace ComplexBot.Services.Trading;

/// <summary>
/// Manages shared capital pool across multiple trading pairs.
/// Thread-safe for concurrent updates from multiple traders.
/// </summary>
public class SharedEquityManager
{
    private decimal _totalEquity;
    private decimal _peakEquity;
    private readonly Dictionary<string, decimal> _symbolAllocations = new();
    private readonly Dictionary<string, decimal> _symbolEquities = new();
    private readonly Dictionary<string, decimal> _symbolRealizedPnL = new();
    private readonly object _lock = new();

    public SharedEquityManager(decimal initialCapital)
    {
        _totalEquity = initialCapital;
        _peakEquity = initialCapital;
    }

    // Capital allocation
    public decimal GetAvailableCapital()
    {
        lock (_lock)
        {
            var allocated = _symbolAllocations.Values.Sum();
            return Math.Max(0, _totalEquity - allocated);
        }
    }

    public decimal GetAllocatedCapital(string symbol)
    {
        lock (_lock)
        {
            return _symbolAllocations.GetValueOrDefault(symbol, 0);
        }
    }

    public void AllocateCapital(string symbol, decimal amount)
    {
        lock (_lock)
        {
            _symbolAllocations[symbol] = amount;
            _symbolEquities[symbol] = amount;
        }
    }

    public void ReleaseCapital(string symbol)
    {
        lock (_lock)
        {
            _symbolAllocations.Remove(symbol);
        }
    }

    // Equity updates
    public void UpdateSymbolEquity(string symbol, decimal equity)
    {
        lock (_lock)
        {
            _symbolEquities[symbol] = equity;
            RecalculateTotalEquity();
        }

        OnEquityUpdate?.Invoke(symbol, equity);
        OnTotalEquityUpdate?.Invoke(_totalEquity);

        // Alert on significant drawdown
        if (TotalDrawdownPercent >= 10)
            OnDrawdownAlert?.Invoke(TotalDrawdownPercent);
    }

    public void RecordTradePnL(string symbol, decimal realizedPnL)
    {
        lock (_lock)
        {
            if (!_symbolRealizedPnL.ContainsKey(symbol))
                _symbolRealizedPnL[symbol] = 0;
            _symbolRealizedPnL[symbol] += realizedPnL;
            RecalculateTotalEquity();
        }
    }

    private void RecalculateTotalEquity()
    {
        _totalEquity = _symbolEquities.Values.Sum();
        if (_totalEquity > _peakEquity)
            _peakEquity = _totalEquity;
    }

    // Portfolio metrics
    public decimal TotalEquity
    {
        get { lock (_lock) return _totalEquity; }
    }

    public decimal TotalDrawdownPercent
    {
        get
        {
            lock (_lock)
            {
                if (_peakEquity == 0) return 0;
                return (_peakEquity - _totalEquity) / _peakEquity * 100;
            }
        }
    }

    public PortfolioSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            var details = _symbolEquities.ToDictionary(
                kvp => kvp.Key,
                kvp => new SymbolEquityInfo(
                    _symbolAllocations.GetValueOrDefault(kvp.Key, 0),
                    kvp.Value,
                    kvp.Value - _symbolAllocations.GetValueOrDefault(kvp.Key, 0),
                    _symbolRealizedPnL.GetValueOrDefault(kvp.Key, 0)
                )
            );

            return new PortfolioSnapshot(
                _totalEquity,
                _peakEquity,
                TotalDrawdownPercent,
                GetAvailableCapital(),
                details
            );
        }
    }

    // Events
    public event Action<string, decimal>? OnEquityUpdate;
    public event Action<decimal>? OnTotalEquityUpdate;
    public event Action<decimal>? OnDrawdownAlert;
}

public record PortfolioSnapshot(
    decimal TotalEquity,
    decimal PeakEquity,
    decimal DrawdownPercent,
    decimal AvailableCapital,
    Dictionary<string, SymbolEquityInfo> SymbolDetails
);

public record SymbolEquityInfo(
    decimal AllocatedCapital,
    decimal CurrentEquity,
    decimal UnrealizedPnL,
    decimal RealizedPnL
);
```

---

## Phase 3: Multi-Pair Live Trader (Generic)

### 3.1 Create `MultiPairLiveTrader.cs`

Location: `ComplexBot/Services/Trading/MultiPairLiveTrader.cs`

```csharp
namespace ComplexBot.Services.Trading;

/// <summary>
/// Coordinates multiple symbol traders with shared capital and portfolio risk management.
/// Generic to support different trader implementations (Binance, Bybit, etc.)
/// </summary>
public class MultiPairLiveTrader<TTrader> : IDisposable
    where TTrader : ISymbolTrader
{
    private readonly Dictionary<string, TTrader> _traders = new();
    private readonly SharedEquityManager _equityManager;
    private readonly PortfolioRiskManager _portfolioRiskManager;
    private readonly MultiPairLiveTradingSettings _settings;
    private readonly TelegramNotifier? _telegram;
    private readonly Func<TradingPairConfig, TTrader> _traderFactory;

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

        _equityManager.OnDrawdownAlert += drawdown =>
        {
            _telegram?.SendMessage($"⚠️ Portfolio drawdown alert: {drawdown:F1}%");
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Create traders for each pair
        foreach (var pairConfig in _settings.TradingPairs)
        {
            AddTradingPair(pairConfig);
        }

        // Start all traders in parallel
        var tasks = _traders.Values.Select(t => t.StartAsync(cancellationToken));
        await Task.WhenAll(tasks);

        // Keep running until cancelled
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
    }

    public async Task StopAsync()
    {
        var tasks = _traders.Values.Select(t => t.StopAsync());
        await Task.WhenAll(tasks);
    }

    public void AddTradingPair(TradingPairConfig config)
    {
        if (_traders.ContainsKey(config.Symbol))
        {
            throw new InvalidOperationException($"Trader for {config.Symbol} already exists");
        }

        // Calculate capital allocation
        var allocation = CalculateSymbolAllocation(config);
        _equityManager.AllocateCapital(config.Symbol, allocation);

        // Create trader via factory
        var trader = _traderFactory(config);

        // Wire events
        WireTraderEvents(config.Symbol, trader);

        // Register with portfolio risk manager
        // Note: This requires RiskManager access - may need interface update

        _traders[config.Symbol] = trader;
    }

    public void RemoveTradingPair(string symbol)
    {
        if (_traders.TryGetValue(symbol, out var trader))
        {
            trader.Dispose();
            _traders.Remove(symbol);
            _equityManager.ReleaseCapital(symbol);
        }
    }

    public IReadOnlyList<string> GetActiveSymbols() => _traders.Keys.ToList();

    public PortfolioSnapshot GetPortfolioSnapshot() => _equityManager.GetSnapshot();

    private void WireTraderEvents(string symbol, TTrader trader)
    {
        trader.OnLog += msg => OnLog?.Invoke(symbol, msg);

        trader.OnSignal += signal =>
        {
            OnSignal?.Invoke(signal);
            _telegram?.SendSignal(signal);
        };

        trader.OnTrade += trade =>
        {
            OnTrade?.Invoke(trade);
            _equityManager.RecordTradePnL(symbol, trade.RealizedPnL);
        };

        trader.OnEquityUpdate += equity =>
        {
            _equityManager.UpdateSymbolEquity(symbol, equity);
        };
    }

    private decimal CalculateSymbolAllocation(TradingPairConfig config)
    {
        return _settings.AllocationMode switch
        {
            CapitalAllocationMode.Equal =>
                _settings.TotalCapital / _settings.TradingPairs.Count,

            CapitalAllocationMode.Weighted =>
                _settings.TotalCapital * (config.WeightPercent ?? (100m / _settings.TradingPairs.Count)) / 100m,

            CapitalAllocationMode.Dynamic =>
                _settings.TotalCapital / _settings.TradingPairs.Count, // TODO: ATR-based

            _ => _settings.TotalCapital / _settings.TradingPairs.Count
        };
    }

    public void Dispose()
    {
        foreach (var trader in _traders.Values)
        {
            trader.Dispose();
        }
        _traders.Clear();
    }
}
```

### 3.2 Factory for Creating Binance Traders

```csharp
// In LiveTradingRunner or separate factory class
public static class TraderFactory
{
    public static BinanceLiveTrader CreateBinanceTrader(
        TradingPairConfig config,
        string apiKey,
        string apiSecret,
        RiskSettings riskSettings,
        bool useTestnet,
        bool paperTrade,
        PortfolioRiskManager portfolioRiskManager,
        SharedEquityManager sharedEquityManager,
        TelegramNotifier? telegram,
        ConfigurationService configService)
    {
        // Create strategy based on config
        var strategy = CreateStrategy(config, configService);

        var settings = new LiveTraderSettings
        {
            Symbol = config.Symbol,
            Interval = KlineIntervalExtensions.Parse(config.Interval),
            UseTestnet = useTestnet,
            PaperTrade = paperTrade
        };

        return new BinanceLiveTrader(
            apiKey,
            apiSecret,
            strategy,
            riskSettings,
            settings,
            telegram,
            portfolioRiskManager,
            sharedEquityManager
        );
    }

    private static IStrategy CreateStrategy(TradingPairConfig config, ConfigurationService configService)
    {
        var cfg = configService.GetConfiguration();

        return config.Strategy.ToUpperInvariant() switch
        {
            "ADX" => new AdxTrendStrategy(
                config.StrategyOverrides?.ToStrategySettings() ?? cfg.Strategy.ToStrategySettings()),
            "RSI" => new RsiStrategy(cfg.RsiStrategy.ToRsiStrategySettings()),
            "MA" => new MaStrategy(cfg.MaStrategy.ToMaStrategySettings()),
            "ENSEMBLE" => StrategyEnsemble.CreateDefault(cfg.Ensemble.ToEnsembleSettings()),
            _ => new AdxTrendStrategy(cfg.Strategy.ToStrategySettings())
        };
    }
}
```

---

## Phase 4: LiveTradingRunner Updates

### 4.1 Modify `LiveTradingRunner.cs`

```csharp
public async Task RunLiveTrading(bool paperTrade)
{
    // ... existing validation code ...

    var config = _configService.GetConfiguration();

    // Check if multi-pair is enabled
    if (config.MultiPairLiveTrading?.Enabled == true)
    {
        await RunMultiPairTrading(paperTrade, config);
        return;
    }

    // ... existing single-pair code ...
}

private async Task RunMultiPairTrading(bool paperTrade, BotConfiguration config)
{
    var multiPairSettings = config.MultiPairLiveTrading!;
    var riskSettings = _settingsService.GetRiskSettings();
    var portfolioRiskSettings = config.PortfolioRisk.ToPortfolioRiskSettings();

    AnsiConsole.MarkupLine($"\n[yellow]═══ MULTI-PAIR {(paperTrade ? "PAPER" : "LIVE")} TRADING ═══[/]\n");

    // Display trading pairs
    DisplayTradingPairs(multiPairSettings);

    // Create shared managers
    var sharedEquity = new SharedEquityManager(multiPairSettings.TotalCapital);
    var portfolioRisk = new PortfolioRiskManager(
        portfolioRiskSettings,
        config.PortfolioRisk.CorrelationGroups
    );

    // Create multi-pair trader with factory
    using var multiTrader = new MultiPairLiveTrader<BinanceLiveTrader>(
        multiPairSettings,
        portfolioRiskSettings,
        config.PortfolioRisk.CorrelationGroups,
        pairConfig => TraderFactory.CreateBinanceTrader(
            pairConfig,
            config.BinanceApi.ApiKey,
            config.BinanceApi.ApiSecret,
            riskSettings,
            config.BinanceApi.UseTestnet,
            paperTrade,
            portfolioRisk,
            sharedEquity,
            _telegram,
            _configService
        ),
        _telegram
    );

    // Wire up dashboard events
    var signalTable = CreateSignalTable();
    multiTrader.OnSignal += signal => AddSignalToTable(signalTable, signal);
    multiTrader.OnPortfolioUpdate += snapshot => UpdateDashboard(snapshot);

    // Start trading
    AnsiConsole.MarkupLine("[green]Starting multi-pair trading... Press Ctrl+C to stop[/]\n");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

    try
    {
        await multiTrader.StartAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        Log.Information("Multi-pair trading stopped by user");
    }

    await multiTrader.StopAsync();
    DisplayFinalSummary(multiTrader.GetPortfolioSnapshot(), signalTable);
}

private void DisplayTradingPairs(MultiPairLiveTradingSettings settings)
{
    var table = new Table()
        .Border(TableBorder.Rounded)
        .AddColumn("Symbol")
        .AddColumn("Interval")
        .AddColumn("Strategy")
        .AddColumn("Allocation");

    foreach (var pair in settings.TradingPairs)
    {
        var weight = settings.AllocationMode == CapitalAllocationMode.Equal
            ? 100m / settings.TradingPairs.Count
            : pair.WeightPercent ?? (100m / settings.TradingPairs.Count);

        table.AddRow(
            $"[cyan]{pair.Symbol}[/]",
            pair.Interval,
            pair.Strategy,
            $"{weight:F0}% ({settings.TotalCapital * weight / 100:N0} USDT)");
    }

    AnsiConsole.Write(table);
    AnsiConsole.MarkupLine($"\n[grey]Total Capital: {settings.TotalCapital:N2} USDT[/]");
    AnsiConsole.MarkupLine($"[grey]Allocation: {settings.AllocationMode}[/]");
    AnsiConsole.MarkupLine($"[grey]Correlation Groups: {(settings.UsePortfolioRiskManager ? "Active" : "Disabled")}[/]\n");
}
```

---

## Phase 5: Non-Interactive Mode Support

### 5.1 Environment Variables

```csharp
// In Program.cs
case "multi-pair":
case "multipair":
    var cfg = configService.GetConfiguration();
    if (cfg.MultiPairLiveTrading?.Enabled != true)
    {
        AnsiConsole.MarkupLine("[red]Multi-pair trading not enabled in config[/]");
        return;
    }
    await liveTradingRunner.RunLiveTrading(paperTrade: true);
    break;
```

### 5.2 Docker Support

```yaml
# docker-compose.yml
services:
  tradingbot:
    environment:
      - TRADING_MODE=multi-pair
      - TRADING_MultiPairLiveTrading__Enabled=true
```

---

## Phase 6: Dashboard and Monitoring

### 6.1 Portfolio Dashboard

```
╭──────────────────── MULTI-PAIR TRADING ────────────────────╮
│ Total Equity: $10,234.56  │  Peak: $10,500.00  │  DD: 2.5% │
├────────────────────────────────────────────────────────────┤
│ Symbol   │ Position │ Entry   │ Current │ P&L     │ Risk  │
├──────────┼──────────┼─────────┼─────────┼─────────┼───────┤
│ BTCUSDT  │ LONG 0.1 │ 42,500  │ 43,100  │ +$60.00 │ 1.2%  │
│ ETHUSDT  │ FLAT     │    -    │  2,450  │    -    │  0%   │
│ SOLUSDT  │ LONG 5.0 │   98.50 │  101.20 │ +$13.50 │ 0.8%  │
├──────────────────────────────────────────────────────────────┤
│ Open: 2/5  │  Heat: 2.0%  │  Corr Risk: OK  │  Groups: 1  │
╰────────────────────────────────────────────────────────────╯
```

### 6.2 Telegram Notifications

Extended for multi-pair:
- Portfolio summary (daily/on-demand)
- Per-symbol trade alerts
- Portfolio-wide drawdown alerts
- Correlation group warnings

---

## Implementation Order (Updated)

### Sprint 1: Abstractions (~3 hours)
1. [ ] Create `ISymbolTrader` interface
2. [ ] Create `SymbolTraderBase<TSettings>` base class
3. [ ] Write unit tests for base class

### Sprint 2: Refactoring (~2 hours)
4. [ ] Refactor `BinanceLiveTrader` to inherit from base
5. [ ] Add `PortfolioRiskManager` and `SharedEquityManager` parameters
6. [ ] Verify existing tests pass

### Sprint 3: Configuration (~2 hours)
7. [ ] Add `MultiPairLiveTradingSettings` to `BotConfiguration.cs`
8. [ ] Add configuration to `appsettings.json`
9. [ ] Create `SharedEquityManager.cs`
10. [ ] Unit tests for SharedEquityManager

### Sprint 4: Core Trading (~4 hours)
11. [ ] Create `MultiPairLiveTrader<T>` generic class
12. [ ] Create `TraderFactory` for Binance traders
13. [ ] Integration tests with testnet

### Sprint 5: Runner Integration (~3 hours)
14. [ ] Update `LiveTradingRunner` for multi-pair mode
15. [ ] Add interactive prompts for pair selection
16. [ ] Add non-interactive mode support

### Sprint 6: Polish (~2 hours)
17. [ ] Implement portfolio dashboard
18. [ ] Extend Telegram notifications
19. [ ] Update CLAUDE.md documentation
20. [ ] End-to-end testing

**Total: ~16 hours of coding**

---

## Files Summary

### New Files (5)
| File | Purpose | LOC |
|------|---------|-----|
| `Services/Trading/ISymbolTrader.cs` | Interface for symbol traders | ~30 |
| `Services/Trading/SymbolTraderBase.cs` | Base class with common logic | ~150 |
| `Services/Trading/SharedEquityManager.cs` | Shared capital tracking | ~100 |
| `Services/Trading/MultiPairLiveTrader.cs` | Coordinates multiple traders | ~200 |
| `Services/Trading/TraderFactory.cs` | Factory for creating traders | ~50 |

### Modified Files (6)
| File | Changes |
|------|---------|
| `Configuration/BotConfiguration.cs` | Add MultiPairLiveTradingSettings |
| `appsettings.json` | Add MultiPairLiveTrading section |
| `Services/Trading/BinanceLiveTrader.cs` | Inherit from base, add portfolio hooks |
| `LiveTradingRunner.cs` | Add multi-pair mode |
| `Program.cs` | Add multi-pair trading mode |
| `CLAUDE.md` | Document new feature |

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    LiveTradingRunner                            │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │            MultiPairLiveTrader<BinanceLiveTrader>         │  │
│  │  ┌─────────────────────────────────────────────────────┐  │  │
│  │  │  SharedEquityManager    PortfolioRiskManager        │  │  │
│  │  │  (shared capital)       (correlation groups)        │  │  │
│  │  └─────────────────────────────────────────────────────┘  │  │
│  │                                                           │  │
│  │  ┌─────────────────┐ ┌─────────────────┐ ┌─────────────┐  │  │
│  │  │ BinanceLive     │ │ BinanceLive     │ │ BinanceLive │  │  │
│  │  │ Trader          │ │ Trader          │ │ Trader      │  │  │
│  │  │ ────────────    │ │ ────────────    │ │ ──────────  │  │  │
│  │  │ Symbol: BTCUSDT │ │ Symbol: ETHUSDT │ │ Symbol: SOL │  │  │
│  │  │ Strategy: ADX   │ │ Strategy: RSI   │ │ Strategy:ADX│  │  │
│  │  │ RiskManager     │ │ RiskManager     │ │ RiskManager │  │  │
│  │  └────────┬────────┘ └────────┬────────┘ └──────┬──────┘  │  │
│  │           │                   │                  │         │  │
│  │           └───────────────────┴──────────────────┘         │  │
│  │                    implements ISymbolTrader                │  │
│  │                    extends SymbolTraderBase<T>             │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
                    ┌─────────────────┐
                    │  Binance API    │
                    │  (REST + WS)    │
                    └─────────────────┘
```

---

## Testing Strategy

### Unit Tests
- `SymbolTraderBaseTests.cs` - base class logic
- `SharedEquityManagerTests.cs` - capital allocation, drawdown
- `MultiPairLiveTraderTests.cs` - coordination with mocks

### Integration Tests
- Multi-pair paper trading on testnet
- Verify position limits enforced
- Verify correlation group limits work

### Manual Testing Checklist
- [ ] Start with 3 pairs on testnet
- [ ] Verify all pairs receive kline updates
- [ ] Verify CorrelationGroups block overlimit positions
- [ ] Verify portfolio drawdown updates
- [ ] Test Ctrl+C graceful shutdown
- [ ] Verify state saved for all positions
