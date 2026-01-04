# Multi-Pair Trading Implementation Plan

## Overview

Implementation of multi-pair trading with shared capital pool in a single application instance.

**Architecture Decision:** Shared capital pool - all trading pairs share total equity for more efficient capital utilization.

**Design Principles:**
- Use interfaces and base classes to reduce code duplication
- Leverage generics for type-safe configuration
- Fix existing dead code (CorrelationGroups not connected to live trading)
- Support Multi-Timeframe analysis on same symbol (e.g., ADX on 4h + RSI filter on 1h)

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
| `MultiPairTradingSettings` | ~60 LOC | P0 |
| `MultiPairLiveTrader<T>` | ~250 LOC | P0 |
| `SharedEquityManager` | ~100 LOC | P0 |
| `SymbolTradingUnit` (multi-timeframe) | ~150 LOC | P0 |
| `ISignalFilter` + implementations | ~100 LOC | P0 |
| Refactor `BinanceLiveTrader` | ~50 LOC changes | P1 |
| `LiveTradingRunner` updates | ~150 LOC | P1 |
| Unit tests | ~250 LOC | P2 |

**Total: ~800-1000 LOC**

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

    // Multi-Timeframe Support
    public StrategyRole Role { get; set; } = StrategyRole.Primary;
    public string? FilterMode { get; set; }        // For Filter role: "Confirm", "Veto", "Score"

    // Optional per-pair strategy overrides
    public StrategyConfigSettings? StrategyOverrides { get; set; }
}

/// <summary>
/// Role of strategy in multi-timeframe setup
/// </summary>
public enum StrategyRole
{
    Primary,    // Generates trading signals (only one per symbol)
    Filter,     // Filters/confirms Primary signals (can have multiple)
    Exit        // Only provides exit signals (e.g., trailing stop strategy)
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
    // BTCUSDT: ADX on 4h as Primary, RSI on 1h as Filter
    {
      "Symbol": "BTCUSDT",
      "Interval": "FourHour",
      "Strategy": "ADX",
      "Role": "Primary",
      "WeightPercent": 40
    },
    {
      "Symbol": "BTCUSDT",
      "Interval": "OneHour",
      "Strategy": "RSI",
      "Role": "Filter",
      "FilterMode": "Confirm"
    },
    // ETHUSDT: Simple single-strategy setup
    {
      "Symbol": "ETHUSDT",
      "Interval": "FourHour",
      "Strategy": "ADX",
      "Role": "Primary",
      "WeightPercent": 30
    },
    // SOLUSDT: MA on 4h as Primary, ADX on 1h as Filter
    {
      "Symbol": "SOLUSDT",
      "Interval": "FourHour",
      "Strategy": "MA",
      "Role": "Primary",
      "WeightPercent": 30
    },
    {
      "Symbol": "SOLUSDT",
      "Interval": "OneHour",
      "Strategy": "ADX",
      "Role": "Filter",
      "FilterMode": "Confirm"
    }
  ]
}
```

**Multi-Timeframe Example Explained:**
- BTCUSDT has ADX (4h) generating signals, RSI (1h) confirming them
- When ADX says BUY and RSI is not overbought → execute trade
- When ADX says BUY but RSI is overbought → skip trade

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

## Phase 3: Multi-Timeframe Support

### 3.1 Create `ISignalFilter` Interface

Location: `ComplexBot/Services/Trading/ISignalFilter.cs`

Purpose: Filter/confirm signals from primary strategy using secondary timeframe data.

```csharp
namespace ComplexBot.Services.Trading;

/// <summary>
/// Interface for filtering trading signals from primary strategy.
/// </summary>
public interface ISignalFilter
{
    string Name { get; }
    FilterMode Mode { get; }

    /// <summary>
    /// Evaluate if the primary signal should be executed.
    /// </summary>
    /// <param name="signal">Signal from primary strategy</param>
    /// <param name="filterState">Current state of filter strategy</param>
    /// <returns>FilterResult with decision and reason</returns>
    FilterResult Evaluate(TradeSignal signal, StrategyState filterState);
}

public enum FilterMode
{
    Confirm,    // Signal passes only if filter agrees
    Veto,       // Signal blocked if filter disagrees
    Score       // Adjusts confidence score (for future weighted execution)
}

public record FilterResult(
    bool Approved,
    string Reason,
    decimal? ConfidenceAdjustment = null  // For Score mode: -1.0 to +1.0
);

public record StrategyState(
    SignalType? LastSignal,
    decimal? IndicatorValue,     // e.g., RSI value, ADX value
    bool IsOverbought,
    bool IsOversold,
    bool IsTrending,
    Dictionary<string, decimal> CustomValues  // Strategy-specific values
);
```

### 3.2 Implement Signal Filters

Location: `ComplexBot/Services/Trading/SignalFilters/`

```csharp
namespace ComplexBot.Services.Trading.SignalFilters;

/// <summary>
/// RSI-based filter: blocks buys when overbought, sells when oversold.
/// </summary>
public class RsiSignalFilter : ISignalFilter
{
    public string Name => "RSI Filter";
    public FilterMode Mode { get; }

    private readonly decimal _overboughtLevel;
    private readonly decimal _oversoldLevel;

    public RsiSignalFilter(FilterMode mode, decimal overbought = 70, decimal oversold = 30)
    {
        Mode = mode;
        _overboughtLevel = overbought;
        _oversoldLevel = oversold;
    }

    public FilterResult Evaluate(TradeSignal signal, StrategyState filterState)
    {
        var rsiValue = filterState.IndicatorValue ?? 50;

        return signal.Type switch
        {
            SignalType.Buy when rsiValue >= _overboughtLevel =>
                new FilterResult(false, $"RSI overbought ({rsiValue:F1} >= {_overboughtLevel})"),

            SignalType.Buy when rsiValue < _overboughtLevel =>
                new FilterResult(true, $"RSI confirms buy ({rsiValue:F1})"),

            SignalType.Sell when rsiValue <= _oversoldLevel =>
                new FilterResult(false, $"RSI oversold ({rsiValue:F1} <= {_oversoldLevel})"),

            SignalType.Sell when rsiValue > _oversoldLevel =>
                new FilterResult(true, $"RSI confirms sell ({rsiValue:F1})"),

            _ => new FilterResult(true, "No filter applied")
        };
    }
}

/// <summary>
/// ADX-based filter: confirms trades only when trend is strong enough.
/// </summary>
public class AdxSignalFilter : ISignalFilter
{
    public string Name => "ADX Trend Filter";
    public FilterMode Mode { get; }

    private readonly decimal _minAdxThreshold;

    public AdxSignalFilter(FilterMode mode, decimal minAdx = 20)
    {
        Mode = mode;
        _minAdxThreshold = minAdx;
    }

    public FilterResult Evaluate(TradeSignal signal, StrategyState filterState)
    {
        var adxValue = filterState.IndicatorValue ?? 0;

        if (adxValue < _minAdxThreshold)
        {
            return new FilterResult(false, $"Trend too weak (ADX {adxValue:F1} < {_minAdxThreshold})");
        }

        return new FilterResult(true, $"Trend confirmed (ADX {adxValue:F1})");
    }
}

/// <summary>
/// Trend alignment filter: confirms only if higher timeframe agrees with direction.
/// </summary>
public class TrendAlignmentFilter : ISignalFilter
{
    public string Name => "Trend Alignment Filter";
    public FilterMode Mode { get; }

    public TrendAlignmentFilter(FilterMode mode = FilterMode.Confirm)
    {
        Mode = mode;
    }

    public FilterResult Evaluate(TradeSignal signal, StrategyState filterState)
    {
        if (!filterState.IsTrending)
        {
            return new FilterResult(false, "No clear trend on higher timeframe");
        }

        // Check if signal direction matches trend
        var trendDirection = filterState.LastSignal;

        if (signal.Type == SignalType.Buy && trendDirection == SignalType.Sell)
        {
            return new FilterResult(false, "Buy signal against downtrend");
        }

        if (signal.Type == SignalType.Sell && trendDirection == SignalType.Buy)
        {
            return new FilterResult(false, "Sell signal against uptrend");
        }

        return new FilterResult(true, "Signal aligned with trend");
    }
}
```

### 3.3 Create `SymbolTradingUnit`

Location: `ComplexBot/Services/Trading/SymbolTradingUnit.cs`

Purpose: Groups Primary strategy + Filter strategies for a single symbol.

```csharp
namespace ComplexBot.Services.Trading;

/// <summary>
/// Manages multiple strategies/timeframes for a single trading symbol.
/// One Primary strategy generates signals, Filter strategies confirm/reject them.
/// </summary>
public class SymbolTradingUnit : IDisposable
{
    private readonly string _symbol;
    private readonly ISymbolTrader _primaryTrader;
    private readonly List<(IStrategy Strategy, KlineInterval Interval, ISignalFilter Filter)> _filters = new();
    private readonly Dictionary<KlineInterval, Candle> _lastCandles = new();
    private readonly Dictionary<KlineInterval, StrategyState> _filterStates = new();

    public string Symbol => _symbol;
    public ISymbolTrader PrimaryTrader => _primaryTrader;
    public decimal CurrentPosition => _primaryTrader.CurrentPosition;

    // Events
    public event Action<string>? OnLog;
    public event Action<TradeSignal, FilterResult>? OnFilteredSignal;

    public SymbolTradingUnit(string symbol, ISymbolTrader primaryTrader)
    {
        _symbol = symbol;
        _primaryTrader = primaryTrader;

        // Intercept signals from primary trader
        _primaryTrader.OnSignal += OnPrimarySignal;
    }

    public void AddFilter(IStrategy strategy, KlineInterval interval, ISignalFilter filter)
    {
        _filters.Add((strategy, interval, filter));
        _filterStates[interval] = new StrategyState(null, null, false, false, false, new());
    }

    public void UpdateFilterCandle(KlineInterval interval, Candle candle)
    {
        _lastCandles[interval] = candle;

        // Find filter for this interval and update its state
        var filterEntry = _filters.FirstOrDefault(f => f.Interval == interval);
        if (filterEntry.Strategy != null)
        {
            var signal = filterEntry.Strategy.Analyze(candle, CurrentPosition, _symbol);
            UpdateFilterState(interval, filterEntry.Strategy, signal);
        }
    }

    private void UpdateFilterState(KlineInterval interval, IStrategy strategy, TradeSignal? signal)
    {
        // Extract strategy state (this needs strategy-specific implementation)
        var state = new StrategyState(
            LastSignal: signal?.Type,
            IndicatorValue: GetIndicatorValue(strategy),
            IsOverbought: IsOverbought(strategy),
            IsOversold: IsOversold(strategy),
            IsTrending: IsTrending(strategy),
            CustomValues: GetCustomValues(strategy)
        );

        _filterStates[interval] = state;
    }

    private void OnPrimarySignal(TradeSignal signal)
    {
        // Apply all filters
        foreach (var (strategy, interval, filter) in _filters)
        {
            if (!_filterStates.TryGetValue(interval, out var state))
                continue;

            var result = filter.Evaluate(signal, state);

            Log($"Filter [{filter.Name}] on {interval}: {(result.Approved ? "✓" : "✗")} {result.Reason}");

            if (!result.Approved && filter.Mode == FilterMode.Confirm)
            {
                Log($"Signal blocked by {filter.Name}");
                OnFilteredSignal?.Invoke(signal, result);
                return;  // Block signal
            }

            if (!result.Approved && filter.Mode == FilterMode.Veto)
            {
                Log($"Signal vetoed by {filter.Name}");
                OnFilteredSignal?.Invoke(signal, result);
                return;  // Veto signal
            }
        }

        // All filters passed - signal is approved
        OnFilteredSignal?.Invoke(signal, new FilterResult(true, "All filters passed"));
    }

    // Helper methods to extract strategy state (needs IStrategy extension)
    private decimal? GetIndicatorValue(IStrategy strategy) =>
        strategy switch
        {
            RsiStrategy rsi => rsi.CurrentRsi,      // Need to expose these
            AdxTrendStrategy adx => adx.CurrentAdx,
            _ => null
        };

    private bool IsOverbought(IStrategy strategy) =>
        strategy is RsiStrategy rsi && rsi.CurrentRsi > 70;

    private bool IsOversold(IStrategy strategy) =>
        strategy is RsiStrategy rsi && rsi.CurrentRsi < 30;

    private bool IsTrending(IStrategy strategy) =>
        strategy is AdxTrendStrategy adx && adx.CurrentAdx > 25;

    private Dictionary<string, decimal> GetCustomValues(IStrategy strategy) => new();

    private void Log(string message)
    {
        OnLog?.Invoke($"[{_symbol}] {message}");
    }

    public void Dispose()
    {
        _primaryTrader.OnSignal -= OnPrimarySignal;
        _primaryTrader.Dispose();
    }
}
```

### 3.4 Extend IStrategy Interface

Add properties to expose indicator values for filtering:

```csharp
public interface IStrategy
{
    string Name { get; }
    decimal? CurrentStopLoss { get; }
    decimal? CurrentAtr { get; }

    // NEW: For multi-timeframe filtering
    decimal? PrimaryIndicatorValue { get; }  // RSI value, ADX value, etc.
    StrategyState GetCurrentState();

    TradeSignal? Analyze(Candle candle, decimal? currentPosition, string symbol);
    void Reset();
}
```

### 3.5 Multi-Timeframe Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    SymbolTradingUnit (BTCUSDT)                  │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                   Primary Trader                         │    │
│  │  ┌─────────────────┐    ┌─────────────────┐             │    │
│  │  │ ADX Strategy    │───▶│ BinanceLive     │             │    │
│  │  │ Interval: 4h    │    │ Trader          │             │    │
│  │  │ Role: Primary   │    │                 │             │    │
│  │  └─────────────────┘    └────────┬────────┘             │    │
│  └──────────────────────────────────┼──────────────────────┘    │
│                                     │                            │
│                              Signal │                            │
│                                     ▼                            │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                   Signal Filter Pipeline                 │    │
│  │                                                          │    │
│  │  ┌─────────────────┐    ┌─────────────────┐             │    │
│  │  │ RSI Strategy    │───▶│ RsiSignalFilter │──┐          │    │
│  │  │ Interval: 1h    │    │ Mode: Confirm   │  │          │    │
│  │  │ Role: Filter    │    └─────────────────┘  │          │    │
│  │  └─────────────────┘                         │          │    │
│  │                                              ▼          │    │
│  │                                    ┌─────────────────┐  │    │
│  │                                    │ Filter Decision │  │    │
│  │                                    │ Approved: Y/N   │  │    │
│  │                                    └────────┬────────┘  │    │
│  └─────────────────────────────────────────────┼───────────┘    │
│                                                │                 │
│                                     If Approved│                 │
│                                                ▼                 │
│                                    ┌─────────────────┐          │
│                                    │ Execute Trade   │          │
│                                    └─────────────────┘          │
└─────────────────────────────────────────────────────────────────┘
```

---

## Phase 4: Multi-Pair Live Trader (Generic)

### 4.1 Create `MultiPairLiveTrader.cs`

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

### 4.2 Factory for Creating Binance Traders

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

## Phase 5: LiveTradingRunner Updates

### 5.1 Modify `LiveTradingRunner.cs`

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

## Phase 6: Non-Interactive Mode Support

### 6.1 Environment Variables

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

### 6.2 Docker Support

```yaml
# docker-compose.yml
services:
  tradingbot:
    environment:
      - TRADING_MODE=multi-pair
      - TRADING_MultiPairLiveTrading__Enabled=true
```

---

## Phase 7: Dashboard and Monitoring

### 7.1 Portfolio Dashboard

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

### 7.2 Telegram Notifications

Extended for multi-pair:
- Portfolio summary (daily/on-demand)
- Per-symbol trade alerts
- Portfolio-wide drawdown alerts
- Correlation group warnings

---

## Phase 8: Multi-Timeframe Optimization

### 8.1 Filter Optimization Parameters

Extend optimization to include filter configuration:

```csharp
public class MultiTimeframeOptimizerSettings
{
    // Primary strategy optimization (existing)
    public StrategyOptimizerSettings PrimarySettings { get; set; } = new();

    // Filter optimization
    public bool OptimizeFilters { get; set; } = true;

    // Which filter intervals to test
    public List<string> FilterIntervalCandidates { get; set; } = new()
    {
        "FifteenMinutes", "ThirtyMinutes", "OneHour", "TwoHour"
    };

    // RSI filter parameters
    public decimal[] RsiOverboughtRange { get; set; } = { 65, 70, 75, 80 };
    public decimal[] RsiOversoldRange { get; set; } = { 20, 25, 30, 35 };

    // ADX filter parameters
    public decimal[] AdxMinThresholdRange { get; set; } = { 15, 20, 25, 30 };

    // Filter modes to test
    public FilterMode[] FilterModesToTest { get; set; } = { FilterMode.Confirm, FilterMode.Veto };

    // Whether to test with/without filters
    public bool TestNoFilterBaseline { get; set; } = true;
}
```

### 8.2 Multi-Timeframe Backtester

```csharp
public class MultiTimeframeBacktester
{
    public async Task<MultiTimeframeBacktestResult> RunBacktestAsync(
        string symbol,
        TradingPairConfig primaryConfig,
        List<TradingPairConfig> filterConfigs,
        List<Candle> primaryCandles,
        Dictionary<KlineInterval, List<Candle>> filterCandles,
        BacktestSettings settings)
    {
        // Create SymbolTradingUnit for backtest
        var mockTrader = new MockSymbolTrader(symbol, primaryCandles);
        var tradingUnit = new SymbolTradingUnit(symbol, mockTrader);

        // Add filters
        foreach (var filterConfig in filterConfigs)
        {
            var strategy = CreateStrategy(filterConfig);
            var filter = CreateFilter(filterConfig);
            var interval = ParseInterval(filterConfig.Interval);
            tradingUnit.AddFilter(strategy, interval, filter);
        }

        // Run simulation
        var trades = new List<Trade>();
        var filteredSignals = new List<(TradeSignal Signal, FilterResult Result)>();

        tradingUnit.OnFilteredSignal += (signal, result) =>
        {
            filteredSignals.Add((signal, result));
            if (result.Approved)
            {
                // Execute trade in simulation
                trades.Add(SimulateTrade(signal, settings));
            }
        };

        // Process all candles chronologically
        await SimulateCandleFlow(tradingUnit, primaryCandles, filterCandles);

        return new MultiTimeframeBacktestResult
        {
            TotalSignals = filteredSignals.Count,
            ApprovedSignals = filteredSignals.Count(s => s.Result.Approved),
            BlockedSignals = filteredSignals.Count(s => !s.Result.Approved),
            FilterEfficiency = CalculateFilterEfficiency(filteredSignals, trades),
            Trades = trades,
            Metrics = CalculateMetrics(trades, settings.InitialCapital)
        };
    }

    private decimal CalculateFilterEfficiency(
        List<(TradeSignal Signal, FilterResult Result)> signals,
        List<Trade> trades)
    {
        // Compare blocked signals vs what would have been losing trades
        var blockedSignals = signals.Where(s => !s.Result.Approved).ToList();
        var wouldHaveLost = 0;

        foreach (var blocked in blockedSignals)
        {
            // Simulate what would have happened
            var hypotheticalPnL = SimulateHypotheticalTrade(blocked.Signal);
            if (hypotheticalPnL < 0)
                wouldHaveLost++;
        }

        if (blockedSignals.Count == 0) return 0;
        return (decimal)wouldHaveLost / blockedSignals.Count * 100;  // % of blocked that would have lost
    }
}

public record MultiTimeframeBacktestResult
{
    public int TotalSignals { get; init; }
    public int ApprovedSignals { get; init; }
    public int BlockedSignals { get; init; }
    public decimal FilterEfficiency { get; init; }  // % of blocked signals that were correct
    public List<Trade> Trades { get; init; } = new();
    public BacktestMetrics Metrics { get; init; } = new();
}
```

### 8.3 Grid Search for Filter Parameters

```csharp
public class MultiTimeframeOptimizer
{
    public async Task<List<MultiTimeframeOptimizationResult>> OptimizeAsync(
        string symbol,
        List<Candle> candles,
        MultiTimeframeOptimizerSettings settings)
    {
        var results = new List<MultiTimeframeOptimizationResult>();

        // 1. Baseline: Primary strategy without filters
        if (settings.TestNoFilterBaseline)
        {
            var baseline = await RunBaselineAsync(symbol, candles, settings.PrimarySettings);
            results.Add(new MultiTimeframeOptimizationResult
            {
                Configuration = "No Filter (Baseline)",
                Metrics = baseline,
                FilterInterval = null,
                FilterStrategy = null
            });
        }

        // 2. Test each filter interval
        foreach (var filterInterval in settings.FilterIntervalCandidates)
        {
            var filterCandles = await LoadCandlesForInterval(symbol, filterInterval);

            // 3. Test RSI filter with different parameters
            foreach (var overbought in settings.RsiOverboughtRange)
            {
                foreach (var oversold in settings.RsiOversoldRange)
                {
                    foreach (var mode in settings.FilterModesToTest)
                    {
                        var result = await TestConfiguration(
                            symbol, candles, filterCandles,
                            "RSI", filterInterval, mode,
                            new { Overbought = overbought, Oversold = oversold }
                        );
                        results.Add(result);
                    }
                }
            }

            // 4. Test ADX filter with different thresholds
            foreach (var minAdx in settings.AdxMinThresholdRange)
            {
                foreach (var mode in settings.FilterModesToTest)
                {
                    var result = await TestConfiguration(
                        symbol, candles, filterCandles,
                        "ADX", filterInterval, mode,
                        new { MinAdxThreshold = minAdx }
                    );
                    results.Add(result);
                }
            }
        }

        // 5. Rank by risk-adjusted return
        return results
            .OrderByDescending(r => r.Metrics.SharpeRatio * (1 - r.Metrics.MaxDrawdownPercent / 100))
            .ToList();
    }
}

public record MultiTimeframeOptimizationResult
{
    public string Configuration { get; init; } = "";
    public string? FilterInterval { get; init; }
    public string? FilterStrategy { get; init; }
    public FilterMode? FilterMode { get; init; }
    public object? FilterParameters { get; init; }
    public BacktestMetrics Metrics { get; init; } = new();
    public decimal FilterEfficiency { get; init; }
    public int SignalsBlocked { get; init; }
}
```

### 8.4 Optimization Output Example

```
╭─────────────────── MULTI-TIMEFRAME OPTIMIZATION RESULTS ───────────────────╮
│ Symbol: BTCUSDT  │  Primary: ADX (4h)  │  Tested: 48 configurations        │
├─────────────────────────────────────────────────────────────────────────────┤
│ Rank │ Filter      │ Interval │ Mode    │ Params       │ Sharpe │ DD%  │ Eff │
├──────┼─────────────┼──────────┼─────────┼──────────────┼────────┼──────┼─────┤
│  1   │ RSI         │ 1h       │ Confirm │ OB:70 OS:30  │  2.14  │ 12%  │ 78% │
│  2   │ RSI         │ 1h       │ Confirm │ OB:75 OS:25  │  2.08  │ 11%  │ 72% │
│  3   │ ADX         │ 1h       │ Confirm │ Min:25       │  1.95  │ 14%  │ 65% │
│  4   │ No Filter   │ -        │ -       │ -            │  1.72  │ 18%  │ -   │
│  5   │ RSI         │ 30m      │ Veto    │ OB:65 OS:35  │  1.68  │ 15%  │ 45% │
├─────────────────────────────────────────────────────────────────────────────┤
│ Best config improves Sharpe by 24% and reduces drawdown by 33% vs baseline │
╰─────────────────────────────────────────────────────────────────────────────╯

Filter Efficiency = % of blocked signals that would have been losing trades
Higher is better - means filter correctly avoided bad trades
```

### 8.5 Add to Configuration

```json
"MultiTimeframeOptimizer": {
  "OptimizeFilters": true,
  "FilterIntervalCandidates": ["FifteenMinutes", "ThirtyMinutes", "OneHour"],
  "RsiOverboughtRange": [65, 70, 75, 80],
  "RsiOversoldRange": [20, 25, 30, 35],
  "AdxMinThresholdRange": [15, 20, 25, 30],
  "FilterModesToTest": ["Confirm", "Veto"],
  "TestNoFilterBaseline": true,
  "OptimizationTarget": "RiskAdjusted"
}
```

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
8. [ ] Add `StrategyRole` and `FilterMode` enums
9. [ ] Add configuration to `appsettings.json`
10. [ ] Create `SharedEquityManager.cs`

### Sprint 4: Multi-Timeframe Support (~4 hours)
11. [ ] Create `ISignalFilter` interface
12. [ ] Implement `RsiSignalFilter`, `AdxSignalFilter`, `TrendAlignmentFilter`
13. [ ] Create `SymbolTradingUnit` class
14. [ ] Extend `IStrategy` with `GetCurrentState()` method
15. [ ] Add `CurrentRsi`, `CurrentAdx` properties to strategies
16. [ ] Unit tests for signal filters

### Sprint 5: Core Trading (~4 hours)
17. [ ] Create `MultiPairLiveTrader<T>` generic class
18. [ ] Create `TraderFactory` for Binance traders
19. [ ] Integrate `SymbolTradingUnit` with `MultiPairLiveTrader`
20. [ ] Integration tests with testnet

### Sprint 6: Runner Integration (~3 hours)
21. [ ] Update `LiveTradingRunner` for multi-pair mode
22. [ ] Add interactive prompts for pair/filter selection
23. [ ] Add non-interactive mode support

### Sprint 7: Multi-Timeframe Optimization (~4 hours)
24. [ ] Create `MultiTimeframeBacktester` class
25. [ ] Create `MultiTimeframeOptimizer` class
26. [ ] Add `MultiTimeframeOptimizerSettings` to configuration
27. [ ] Integrate with `OptimizationRunner`
28. [ ] Add optimization results display

### Sprint 8: Polish (~2 hours)
29. [ ] Implement portfolio dashboard
30. [ ] Extend Telegram notifications
31. [ ] Update CLAUDE.md documentation
32. [ ] End-to-end testing

**Total: ~24 hours of coding**

---

## Files Summary

### New Files (12)
| File | Purpose | LOC |
|------|---------|-----|
| `Services/Trading/ISymbolTrader.cs` | Interface for symbol traders | ~30 |
| `Services/Trading/SymbolTraderBase.cs` | Base class with common logic | ~150 |
| `Services/Trading/SharedEquityManager.cs` | Shared capital tracking | ~100 |
| `Services/Trading/MultiPairLiveTrader.cs` | Coordinates multiple traders | ~200 |
| `Services/Trading/TraderFactory.cs` | Factory for creating traders | ~50 |
| `Services/Trading/ISignalFilter.cs` | Signal filter interface | ~40 |
| `Services/Trading/SymbolTradingUnit.cs` | Multi-timeframe coordinator | ~150 |
| `Services/Trading/SignalFilters/RsiSignalFilter.cs` | RSI-based filter | ~40 |
| `Services/Trading/SignalFilters/AdxSignalFilter.cs` | ADX trend filter | ~30 |
| `Services/Backtesting/MultiTimeframeBacktester.cs` | Backtest with filters | ~150 |
| `Services/Backtesting/MultiTimeframeOptimizer.cs` | Optimize filter params | ~200 |
| `Services/Trading/MockSymbolTrader.cs` | Mock trader for backtesting | ~80 |

### Modified Files (10)
| File | Changes |
|------|---------|
| `Configuration/BotConfiguration.cs` | Add MultiPairLiveTradingSettings, StrategyRole, FilterMode, MultiTimeframeOptimizerSettings |
| `appsettings.json` | Add MultiPairLiveTrading and MultiTimeframeOptimizer sections |
| `Services/Trading/BinanceLiveTrader.cs` | Inherit from base, add portfolio hooks |
| `Services/Strategies/IStrategy.cs` | Add GetCurrentState(), PrimaryIndicatorValue |
| `Services/Strategies/RsiStrategy.cs` | Expose CurrentRsi property |
| `Services/Strategies/AdxTrendStrategy.cs` | Expose CurrentAdx property |
| `LiveTradingRunner.cs` | Add multi-pair mode |
| `OptimizationRunner.cs` | Add multi-timeframe optimization mode |
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
- `RsiSignalFilterTests.cs` - RSI filter logic
- `AdxSignalFilterTests.cs` - ADX filter logic
- `SymbolTradingUnitTests.cs` - multi-timeframe signal flow
- `MultiTimeframeBacktesterTests.cs` - backtest with filters
- `MultiTimeframeOptimizerTests.cs` - optimization grid search

### Integration Tests
- Multi-pair paper trading on testnet
- Verify position limits enforced
- Verify correlation group limits work
- Multi-timeframe optimization on historical data

### Manual Testing Checklist
- [ ] Start with 3 pairs on testnet
- [ ] Verify all pairs receive kline updates
- [ ] Verify CorrelationGroups block overlimit positions
- [ ] Verify portfolio drawdown updates
- [ ] Test Ctrl+C graceful shutdown
- [ ] Verify state saved for all positions
- [ ] Test multi-timeframe: ADX 4h + RSI 1h filter
- [ ] Verify filter blocks signals correctly
- [ ] Run multi-timeframe optimization
- [ ] Compare optimization results with/without filters
