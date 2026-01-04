# Multi-Pair Trading Implementation Plan

## Overview

Implementation of multi-pair trading with shared capital pool in a single application instance.

**Architecture Decision:** Shared capital pool - all trading pairs share total equity for more efficient capital utilization.

## Current State Analysis

### Already Implemented (Ready to Use)
| Component | Status | Location |
|-----------|--------|----------|
| `PortfolioRiskManager` | Complete | `Services/RiskManagement/PortfolioRiskManager.cs` |
| `AggregatedEquityTracker` | Complete | `Services/RiskManagement/AggregatedEquityTracker.cs` |
| `StateManager` (multi-position) | Complete | `Services/State/StateManager.cs` |
| Correlation groups config | Complete | `appsettings.json` -> `PortfolioRisk` |
| All models have Symbol field | Complete | `Models/` |

### Needs Implementation
| Component | Effort | Priority |
|-----------|--------|----------|
| `MultiPairTradingSettings` | ~50 LOC | P0 |
| `MultiPairLiveTrader` | ~300 LOC | P0 |
| `SharedEquityManager` | ~100 LOC | P0 |
| `LiveTradingRunner` updates | ~150 LOC | P1 |
| Non-interactive mode support | ~50 LOC | P1 |
| Unit tests | ~200 LOC | P2 |

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
public class SharedEquityManager
{
    private decimal _totalEquity;
    private decimal _peakEquity;
    private readonly Dictionary<string, decimal> _symbolAllocations;  // Symbol -> allocated capital
    private readonly Dictionary<string, decimal> _symbolEquities;     // Symbol -> current equity
    private readonly Dictionary<string, decimal> _symbolPnL;          // Symbol -> unrealized P&L
    private readonly object _lock = new();

    public SharedEquityManager(decimal initialCapital);

    // Capital allocation
    public decimal GetAvailableCapital();
    public decimal GetAllocatedCapital(string symbol);
    public void AllocateCapital(string symbol, decimal amount);
    public void ReleaseCapital(string symbol, decimal amount);

    // Equity updates
    public void UpdateSymbolEquity(string symbol, decimal equity);
    public void RecordTradePnL(string symbol, decimal realizedPnL);

    // Portfolio metrics
    public decimal TotalEquity { get; }
    public decimal TotalDrawdownPercent { get; }
    public decimal GetSymbolDrawdownPercent(string symbol);
    public PortfolioSnapshot GetSnapshot();

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

### 2.2 Integration Points

The `SharedEquityManager` will:
1. Be created once in `MultiPairLiveTrader`
2. Be passed to each `BinanceLiveTrader` instance
3. Update on every `OnEquityUpdate` event from traders
4. Coordinate with `PortfolioRiskManager` for risk checks

---

## Phase 3: Multi-Pair Live Trader

### 3.1 Create `MultiPairLiveTrader.cs`

Location: `ComplexBot/Services/Trading/MultiPairLiveTrader.cs`

```csharp
public class MultiPairLiveTrader : IDisposable
{
    private readonly Dictionary<string, BinanceLiveTrader> _traders;
    private readonly Dictionary<string, IStrategy> _strategies;
    private readonly SharedEquityManager _equityManager;
    private readonly PortfolioRiskManager _portfolioRiskManager;
    private readonly TelegramNotifier? _telegram;
    private readonly MultiPairLiveTradingSettings _settings;
    private readonly CancellationTokenSource _cts;

    // Events (aggregated from all traders)
    public event Action<string, string>? OnLog;           // symbol, message
    public event Action<TradeSignal>? OnSignal;
    public event Action<Trade>? OnTrade;
    public event Action<PortfolioSnapshot>? OnPortfolioUpdate;

    public MultiPairLiveTrader(
        string apiKey,
        string apiSecret,
        MultiPairLiveTradingSettings settings,
        RiskSettings riskSettings,
        PortfolioRiskSettings portfolioRiskSettings,
        Dictionary<string, string[]>? correlationGroups,
        TelegramNotifier? telegram = null);

    // Lifecycle
    public async Task StartAsync(CancellationToken cancellationToken = default);
    public async Task StopAsync();

    // Management
    public void AddTradingPair(TradingPairConfig config);
    public void RemoveTradingPair(string symbol);
    public IReadOnlyList<string> GetActiveSymbols();

    // State
    public PortfolioSnapshot GetPortfolioSnapshot();
    public List<StateManager.SavedPosition> GetAllOpenPositions();

    // Private methods
    private BinanceLiveTrader CreateTraderForSymbol(TradingPairConfig config);
    private IStrategy CreateStrategyForSymbol(TradingPairConfig config);
    private void WireTraderEvents(string symbol, BinanceLiveTrader trader);
    private decimal CalculateSymbolAllocation(TradingPairConfig config);
    private bool CanOpenPositionWithPortfolioCheck(string symbol);
}
```

### 3.2 Key Implementation Details

#### Constructor Flow:
```
1. Create SharedEquityManager with TotalCapital
2. Create PortfolioRiskManager with settings and correlation groups
3. For each TradingPair in settings:
   a. Create strategy instance via StrategyFactory
   b. Calculate capital allocation
   c. Create BinanceLiveTrader with allocated capital
   d. Register symbol in PortfolioRiskManager
   e. Wire up events
```

#### StartAsync Flow:
```
1. Get initial account balance from Binance
2. Initialize SharedEquityManager with actual balance
3. Start all traders in parallel:
   var tasks = _traders.Values.Select(t => t.StartAsync(cts.Token));
   await Task.WhenAll(tasks);
4. Begin portfolio monitoring loop
```

#### Position Opening Hook:
```csharp
// In BinanceLiveTrader.ProcessSignalAsync, before opening position:
private async Task ProcessSignalAsync(TradeSignal signal, Candle candle)
{
    // New: Check portfolio-level risk
    if (!_portfolioRiskManager?.CanOpenPosition(_settings.Symbol) ?? false)
    {
        Log($"Portfolio risk check failed for {_settings.Symbol}");
        return;
    }

    // Existing logic...
}
```

### 3.3 Modify `BinanceLiveTrader` for Multi-Pair Support

Add optional constructor parameter and integration:

```csharp
public class BinanceLiveTrader : IDisposable
{
    // New fields
    private readonly PortfolioRiskManager? _portfolioRiskManager;
    private readonly SharedEquityManager? _sharedEquityManager;

    // Extended constructor
    public BinanceLiveTrader(
        string apiKey,
        string apiSecret,
        IStrategy strategy,
        RiskSettings riskSettings,
        LiveTraderSettings? settings = null,
        TelegramNotifier? telegram = null,
        PortfolioRiskManager? portfolioRiskManager = null,    // NEW
        SharedEquityManager? sharedEquityManager = null)       // NEW
    {
        // ... existing code ...
        _portfolioRiskManager = portfolioRiskManager;
        _sharedEquityManager = sharedEquityManager;
    }

    // Modify ProcessSignalAsync to check portfolio risk
    private async Task ProcessSignalAsync(TradeSignal signal, Candle candle)
    {
        switch (signal.Type)
        {
            case SignalType.Buy when _currentPosition <= 0:
                // NEW: Portfolio-level check
                if (_portfolioRiskManager != null &&
                    !_portfolioRiskManager.CanOpenPosition(_settings.Symbol))
                {
                    Log($"⛔ Portfolio risk limit - cannot open position");
                    return;
                }
                // ... existing logic ...
        }
    }

    // Modify equity updates to notify SharedEquityManager
    private void UpdateEquityTracking(decimal equity)
    {
        _riskManager.UpdateEquity(equity);
        _sharedEquityManager?.UpdateSymbolEquity(_settings.Symbol, equity);
        OnEquityUpdate?.Invoke(equity);
    }
}
```

---

## Phase 4: LiveTradingRunner Updates

### 4.1 Modify `LiveTradingRunner.cs`

Add multi-pair mode selection and execution:

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
    var multiPairSettings = config.MultiPairLiveTrading;

    AnsiConsole.MarkupLine($"\n[yellow]═══ MULTI-PAIR {(paperTrade ? "PAPER" : "LIVE")} TRADING ═══[/]\n");

    // Display trading pairs
    var pairsTable = new Table()
        .Border(TableBorder.Rounded)
        .AddColumn("Symbol")
        .AddColumn("Interval")
        .AddColumn("Strategy")
        .AddColumn("Weight");

    foreach (var pair in multiPairSettings.TradingPairs)
    {
        var weight = CalculateWeight(pair, multiPairSettings);
        pairsTable.AddRow(
            pair.Symbol,
            pair.Interval,
            pair.Strategy,
            $"{weight:P0}");
    }

    AnsiConsole.Write(pairsTable);
    AnsiConsole.MarkupLine($"\n[grey]Total Capital: {multiPairSettings.TotalCapital:N2} USDT[/]");
    AnsiConsole.MarkupLine($"[grey]Allocation Mode: {multiPairSettings.AllocationMode}[/]\n");

    // Create multi-pair trader
    var riskSettings = _settingsService.GetRiskSettings();
    var portfolioRiskSettings = config.PortfolioRisk.ToPortfolioRiskSettings();

    using var multiTrader = new MultiPairLiveTrader(
        config.BinanceApi.ApiKey,
        config.BinanceApi.ApiSecret,
        multiPairSettings,
        riskSettings,
        portfolioRiskSettings,
        config.PortfolioRisk.CorrelationGroups,
        telegram);

    // Wire up events
    var signalTable = new Table()
        .Border(TableBorder.Rounded)
        .AddColumn("Time")
        .AddColumn("Symbol")
        .AddColumn("Signal")
        .AddColumn("Price")
        .AddColumn("Reason");

    multiTrader.OnSignal += signal =>
    {
        signalTable.AddRow(
            DateTime.UtcNow.ToString("HH:mm:ss"),
            signal.Symbol,
            signal.Type.ToString(),
            $"{signal.Price:F2}",
            signal.Reason ?? "");
    };

    multiTrader.OnPortfolioUpdate += snapshot =>
    {
        // Update live dashboard
        UpdateDashboard(snapshot);
    };

    // Start trading
    AnsiConsole.MarkupLine("[green]Starting multi-pair trading... Press Ctrl+C to stop[/]\n");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (s, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    try
    {
        await multiTrader.StartAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        Log.Information("Multi-pair trading stopped by user");
    }

    await multiTrader.StopAsync();

    // Display final summary
    DisplayFinalSummary(multiTrader.GetPortfolioSnapshot(), signalTable);
}
```

### 4.2 Interactive Mode Prompts

For interactive mode, add pair selection UI:

```csharp
private MultiPairLiveTradingSettings PromptMultiPairSettings(BotConfiguration config)
{
    var settings = new MultiPairLiveTradingSettings
    {
        Enabled = true,
        TotalCapital = SpectreHelpers.AskDecimal("Total capital (USDT)", 10000m, min: 100m)
    };

    // Select allocation mode
    settings.AllocationMode = AnsiConsole.Prompt(
        new SelectionPrompt<CapitalAllocationMode>()
            .Title("Capital allocation mode:")
            .AddChoices(Enum.GetValues<CapitalAllocationMode>()));

    // Add trading pairs
    var availableSymbols = new[] { "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "ADAUSDT", "DOTUSDT" };
    var selectedSymbols = AnsiConsole.Prompt(
        new MultiSelectionPrompt<string>()
            .Title("Select trading pairs:")
            .AddChoices(availableSymbols)
            .Required());

    foreach (var symbol in selectedSymbols)
    {
        var strategy = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"Strategy for {symbol}:")
                .AddChoices("ADX", "RSI", "MA", "Ensemble"));

        settings.TradingPairs.Add(new TradingPairConfig
        {
            Symbol = symbol,
            Interval = "FourHour",
            Strategy = strategy
        });
    }

    return settings;
}
```

---

## Phase 5: Non-Interactive Mode Support

### 5.1 Environment Variables

Add support for `TRADING_MODE=multi-pair`:

```csharp
// In Program.cs
case "multi-pair":
case "multipair":
    await liveTradingRunner.RunLiveTrading(paperTrade: true);  // Uses config
    break;
```

### 5.2 Configuration-Driven Execution

When `TRADING_MODE=multi-pair` and `MultiPairLiveTrading.Enabled=true`:
- Skip interactive prompts
- Use all settings from `appsettings.json`
- Log configuration at startup for verification

---

## Phase 6: Dashboard and Monitoring

### 6.1 Portfolio Dashboard

Create live updating dashboard showing:

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
│ Open Positions: 2/5  │  Portfolio Heat: 2.0%  │  Corr: OK │
╰────────────────────────────────────────────────────────────╯
```

### 6.2 Telegram Notifications

Extend notifications to include:
- Portfolio summary (daily/on-demand)
- Position opened/closed for each symbol
- Portfolio-wide risk alerts
- Correlation group alerts

---

## Implementation Order

### Sprint 1: Foundation (Estimated ~4 hours of coding)
1. [ ] Add `MultiPairLiveTradingSettings` to `BotConfiguration.cs`
2. [ ] Add configuration to `appsettings.json`
3. [ ] Create `SharedEquityManager.cs`
4. [ ] Write unit tests for SharedEquityManager

### Sprint 2: Core Trading (Estimated ~6 hours of coding)
5. [ ] Create `MultiPairLiveTrader.cs`
6. [ ] Modify `BinanceLiveTrader` - add portfolio integration
7. [ ] Update `StrategyFactory` for per-symbol strategy creation
8. [ ] Integration tests with testnet

### Sprint 3: Runner Integration (Estimated ~3 hours of coding)
9. [ ] Update `LiveTradingRunner` for multi-pair mode
10. [ ] Add interactive prompts for pair selection
11. [ ] Add non-interactive mode support (`TRADING_MODE=multi-pair`)

### Sprint 4: Polish (Estimated ~2 hours of coding)
12. [ ] Implement portfolio dashboard
13. [ ] Extend Telegram notifications
14. [ ] Update CLAUDE.md documentation
15. [ ] End-to-end testing

---

## Risk Considerations

### Capital Allocation
With shared capital:
- A losing trade on one pair reduces available capital for all pairs
- Winning trades increase available capital for new positions
- Drawdown is calculated at portfolio level

### Position Sizing Adjustment
```csharp
// Per-pair position sizing with shared capital
public decimal CalculatePositionSize(string symbol, decimal entryPrice, decimal stopLoss)
{
    // Get available capital (total - allocated)
    var availableCapital = _sharedEquityManager.GetAvailableCapital();

    // Apply risk per trade (e.g., 1% of available)
    var riskAmount = availableCapital * _riskSettings.RiskPerTradePercent / 100m;

    // Calculate position size based on stop distance
    var stopDistance = Math.Abs(entryPrice - stopLoss);
    var positionSize = riskAmount / stopDistance;

    return positionSize;
}
```

### Concurrent Position Limits
- `PortfolioRiskManager.MaxConcurrentPositions` = 5 (configurable)
- Prevents over-allocation during multiple simultaneous signals

### Correlation Risk
- BTC + ETH in same group limits combined exposure
- `MaxCorrelatedRiskPercent` = 10% prevents overweight in correlated assets

---

## Files Summary

### New Files
| File | Purpose |
|------|---------|
| `Services/Trading/SharedEquityManager.cs` | Shared capital tracking |
| `Services/Trading/MultiPairLiveTrader.cs` | Coordinates multiple traders |

### Modified Files
| File | Changes |
|------|---------|
| `Configuration/BotConfiguration.cs` | Add MultiPairLiveTradingSettings |
| `appsettings.json` | Add MultiPairLiveTrading section |
| `Services/Trading/BinanceLiveTrader.cs` | Add portfolio integration hooks |
| `LiveTradingRunner.cs` | Add multi-pair mode |
| `Program.cs` | Add multi-pair trading mode |
| `CLAUDE.md` | Document new feature |

---

## Testing Strategy

### Unit Tests
- `SharedEquityManagerTests.cs` - capital allocation, drawdown calculation
- `MultiPairLiveTraderTests.cs` - mocked traders coordination

### Integration Tests
- Multi-pair paper trading on testnet
- Verify position limits enforced
- Verify correlation group limits

### Manual Testing Checklist
- [ ] Start with 3 pairs on testnet
- [ ] Verify all pairs receive kline updates
- [ ] Trigger signal on one pair, verify position opens
- [ ] Verify portfolio drawdown updates
- [ ] Test Ctrl+C graceful shutdown
- [ ] Verify state saved for all positions
