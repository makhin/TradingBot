# Multi-Assets Mode: Research & Implementation Plan

## 1. Problem Statement

SignalBot receives trading signals with USDT-denominated symbols (e.g., `BTCUSDT`, `ETHUSDT`). The bot is configured to trade on USDC perpetual contracts, converting `BTCUSDT` -> `BTCUSDC`. However, **not all symbols have USDC perpetual contracts** on Binance Futures. When a signal arrives for a symbol without a USDC pair, the bot currently **skips the signal entirely**, missing potential trades.

**Current flow (SignalBotRunner.cs:236-267):**
```
Signal: XYZUSDT -> NormalizeSignalSymbol() -> XYZUSDC -> check cache -> NOT FOUND -> skip signal
```

## 2. What is Multi-Assets Mode

Binance Multi-Assets Mode allows traders to use **multiple assets as shared collateral** for USDS-M Futures. When enabled:

- USDC, USDT, BTC, ETH, BNB, XRP, ADA, DOT, SOL can all serve as margin collateral
- Margin is shared across both USDT-margined and USDC-margined contracts
- A trader holding USDC can open positions on USDT-margined contracts -- Binance automatically accounts for the cross-asset collateral
- PnL remains denominated in the contract's quote asset (USDT contracts earn/lose USDT, USDC contracts earn/lose USDC)
- An auto-exchange mechanism converts assets when wallet balance for one asset goes below a threshold (e.g., -5,000 USDT for regular users)

**API Endpoints:**
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/fapi/v1/multiAssetsMargin` | POST | Enable/disable Multi-Assets Mode |
| `/fapi/v1/multiAssetsMargin` | GET | Get current Multi-Assets Mode status |
| `/fapi/v1/assetIndex` | GET | Get asset index prices for cross-collateral valuation |

**Binance.Net (v12.0.1) C# methods:**
```csharp
// Enable Multi-Assets Mode
await restClient.UsdFuturesApi.Account.ChangeMultiAssetsModeAsync(true);

// Check current mode
await restClient.UsdFuturesApi.Account.GetMultiAssetsModeAsync();
```

**Sources:**
- [Binance Multi-Assets Mode FAQ](https://www.binance.com/en/support/faq/frequently-asked-questions-on-the-multi-assets-mode-f80415bea8124f6494566f23c0bf9c38)
- [Binance API: Change Multi-Assets Mode](https://developers.binance.com/docs/derivatives/usds-margined-futures/trade/rest-api/Change-Multi-Assets-Mode)
- [Binance API: Multi-Assets Mode Asset Index](https://developers.binance.com/docs/derivatives/usds-margined-futures/market-data/rest-api/Multi-Assets-Mode-Asset-Index)
- [What is Multi-Assets Mode](https://www.binance.com/en/support/faq/what-is-multi-assets-mode-and-which-assets-are-supported-29b45c485d664028b9ca1cdf90b24f6f)

## 3. Critical Constraint: Cross Margin Requirement

**Multi-Assets Mode requires Cross Margin Mode.** Isolated Margin and Multi-Assets Mode are mutually exclusive.

The bot currently uses **Isolated Margin** (`SignalTrader.cs:88`):
```csharp
var marginTypeSet = await _client.SetMarginTypeAsync(signal.Symbol, MarginType.Isolated, ct);
```

**Implications:**
- Isolated Margin: each position has its own dedicated margin. If the position is liquidated, only the margin allocated to that position is lost. Safer for individual positions.
- Cross Margin: all positions share the account's total available balance as margin. A losing position can consume margin from other positions, potentially cascading liquidations.
- When Multi-Assets Mode is enabled, **all symbols** on the account switch to Cross Margin. This is a global account-level setting, not per-symbol.

**This is the biggest architectural decision in this feature.** The bot would need to either:
1. Run entirely in Cross Margin + Multi-Assets Mode (simpler but riskier)
2. Use a hybrid approach with a separate sub-account (complex but safer)

## 4. Proposed Architecture: Symbol Fallback with Multi-Assets Mode

### 4.1 High-Level Flow

```
Signal: XYZUSDT
    |
    v
NormalizeSignalSymbol() -> XYZUSDC
    |
    v
Is XYZUSDC available? --YES--> Trade XYZUSDC (current flow, Isolated Margin)
    |
    NO
    |
    v
Is Multi-Assets Mode enabled in config? --NO--> Skip signal (current behavior)
    |
    YES
    |
    v
Is XYZUSDT available? --NO--> Skip signal
    |
    YES
    |
    v
Ensure Multi-Assets Mode is active on Binance
    |
    v
Trade XYZUSDT with Cross Margin (USDC serves as cross-collateral)
    |
    v
Track position with "fallback" flag for monitoring
```

### 4.2 Configuration Changes

**File: `SignalBot/Configuration/TradingSettings.cs`**

Add new settings:
```csharp
public class TradingSettings
{
    // ... existing settings ...

    /// <summary>
    /// When true, if the preferred symbol (e.g., XYZUSDC) is not available,
    /// fall back to the signal's original symbol (e.g., XYZUSDT) using
    /// Binance Multi-Assets Mode with Cross Margin.
    /// </summary>
    public bool EnableMultiAssetsFallback { get; set; } = false;

    /// <summary>
    /// Which margin type to use for fallback positions (always Cross for Multi-Assets Mode).
    /// This setting is informational -- Multi-Assets Mode enforces Cross Margin.
    /// </summary>
    public string FallbackMarginType { get; set; } = "Cross";
}
```

**File: `appsettings.json`**
```json
"Trading": {
    "SignalSymbolSuffix": "USDT",
    "DefaultSymbolSuffix": "USDC",
    "EnableMultiAssetsFallback": false,
    "MarginType": "Isolated",
    ...
}
```

### 4.3 Implementation Plan (Step by Step)

#### Step 1: Add Multi-Assets Mode Support to BinanceFuturesClient

**File: `TradingBot.Binance/Futures/Interfaces/IBinanceFuturesClient.cs`**

Add new interface methods:
```csharp
/// <summary>
/// Gets the current Multi-Assets Mode status.
/// </summary>
Task<bool> GetMultiAssetsModeAsync(CancellationToken ct = default);

/// <summary>
/// Enables or disables Multi-Assets Mode for the account.
/// NOTE: Multi-Assets Mode requires Cross Margin Mode on all symbols.
/// </summary>
Task<bool> SetMultiAssetsModeAsync(bool enabled, CancellationToken ct = default);
```

**File: `TradingBot.Binance/Futures/BinanceFuturesClient.cs`**

Implement the methods:
```csharp
public async Task<bool> GetMultiAssetsModeAsync(CancellationToken ct = default)
{
    var result = await _client.UsdFuturesApi.Account.GetMultiAssetsModeAsync(ct);
    if (!result.Success)
    {
        _logger.Error("Failed to get Multi-Assets Mode status: {Error}", result.Error?.Message);
        throw new Exception($"Failed to get Multi-Assets Mode: {result.Error?.Message}");
    }
    return result.Data.IsMultiAssetsMode;
}

public async Task<bool> SetMultiAssetsModeAsync(bool enabled, CancellationToken ct = default)
{
    _logger.Information("Setting Multi-Assets Mode to {Enabled}", enabled);
    var result = await _client.UsdFuturesApi.Account.ChangeMultiAssetsModeAsync(enabled, ct);
    if (!result.Success)
    {
        _logger.Error("Failed to set Multi-Assets Mode: {Error}", result.Error?.Message);
        return false;
    }
    _logger.Information("Multi-Assets Mode set to {Enabled}", enabled);
    return true;
}
```

#### Step 2: Add Symbol Fallback Logic to SignalBotRunner

**File: `SignalBot/SignalBotRunner.cs`**

Modify `HandleSignalReceived` to implement fallback logic. The key change is in the section after the early symbol cache check (lines 251-267) and after `EnsureExecutionSymbolSupportedAsync` (line 269):

```csharp
// Current behavior: skip if USDC symbol not available
// New behavior: try to fall back to USDT symbol if Multi-Assets fallback is enabled

if (_availableUsdcSymbols != null && !_availableUsdcSymbols.Contains(normalizedSignal.Symbol))
{
    if (_settings.Trading.EnableMultiAssetsFallback)
    {
        // Try falling back to the original signal symbol (USDT)
        var fallbackResult = await TryFallbackToOriginalSymbolAsync(signal, normalizedSignal);
        if (fallbackResult == null)
        {
            return; // No fallback possible
        }
        normalizedSignal = fallbackResult;
        // Mark that this signal uses Multi-Assets fallback (for margin type handling)
    }
    else
    {
        // Current behavior: skip
        _logger.Warning("Symbol {Symbol} is not available...", normalizedSignal.Symbol);
        await SendNotificationAsync(...);
        return;
    }
}
```

Add a new method:
```csharp
private async Task<TradingSignal?> TryFallbackToOriginalSymbolAsync(
    TradingSignal originalSignal, TradingSignal normalizedSignal)
{
    var signalSuffix = _settings.Trading.SignalSymbolSuffix?.Trim().ToUpperInvariant() ?? "USDT";
    var originalSymbol = originalSignal.Symbol; // e.g., XYZUSDT

    _logger.Information(
        "Preferred symbol {Preferred} not available. Attempting Multi-Assets fallback to {Original}",
        normalizedSignal.Symbol, originalSymbol);

    // Check if the original symbol exists
    if (!await _client.SymbolExistsAsync(originalSymbol, _cts!.Token))
    {
        _logger.Warning("Fallback symbol {Symbol} also not available. Skipping signal.", originalSymbol);
        await SendNotificationAsync($"⚠️ Neither {normalizedSignal.Symbol} nor {originalSymbol} available");
        return null;
    }

    // Ensure Multi-Assets Mode is enabled on the account
    if (!await EnsureMultiAssetsModeEnabled())
    {
        return null;
    }

    _logger.Information("Falling back to {Symbol} with Multi-Assets Mode (Cross Margin)", originalSymbol);
    await SendNotificationAsync(
        $"ℹ️ Multi-Assets Fallback\n" +
        $"Preferred: {normalizedSignal.Symbol} (not available)\n" +
        $"Fallback: {originalSymbol}\n" +
        $"Margin: Cross (Multi-Assets Mode)");

    // Return the original signal symbol with a metadata flag
    return originalSignal with { IsMultiAssetsFallback = true };
}
```

#### Step 3: Handle Margin Type per Position

**File: `SignalBot/Services/Trading/SignalTrader.cs`**

Modify `ExecuteSignalAsync` to set the correct margin type based on whether this is a fallback trade:

```csharp
// Current code (line 88):
var marginTypeSet = await _client.SetMarginTypeAsync(signal.Symbol, MarginType.Isolated, ct);

// New code:
var marginType = signal.IsMultiAssetsFallback
    ? MarginType.Cross    // Multi-Assets Mode requires Cross Margin
    : MarginType.Isolated; // Normal positions use Isolated
var marginTypeSet = await _client.SetMarginTypeAsync(signal.Symbol, marginType, ct);
```

#### Step 4: Add IsMultiAssetsFallback Flag to TradingSignal

**File: `TradingBot.Core/Models/TradingSignal.cs`** (or wherever TradingSignal is defined)

Add a flag to the record:
```csharp
public record TradingSignal
{
    // ... existing properties ...

    /// <summary>
    /// Indicates this signal is trading on a fallback symbol
    /// using Multi-Assets Mode with Cross Margin.
    /// </summary>
    public bool IsMultiAssetsFallback { get; init; } = false;
}
```

#### Step 5: Multi-Assets Mode Lifecycle Management

**File: `SignalBot/SignalBotRunner.cs`**

Add Multi-Assets Mode management:

```csharp
private bool _multiAssetsModeEnabled = false;

private async Task<bool> EnsureMultiAssetsModeEnabled()
{
    if (_multiAssetsModeEnabled)
        return true;

    try
    {
        // Check current status
        var currentMode = await _client.GetMultiAssetsModeAsync(_cts!.Token);
        if (currentMode)
        {
            _multiAssetsModeEnabled = true;
            return true;
        }

        // Enable Multi-Assets Mode
        // WARNING: This switches ALL positions to Cross Margin
        var result = await _client.SetMultiAssetsModeAsync(true, _cts!.Token);
        if (result)
        {
            _multiAssetsModeEnabled = true;
            _logger.Information("Multi-Assets Mode enabled on Binance account");
            return true;
        }

        _logger.Error("Failed to enable Multi-Assets Mode");
        return false;
    }
    catch (Exception ex)
    {
        _logger.Error(ex, "Error enabling Multi-Assets Mode");
        return false;
    }
}
```

#### Step 6: Cache Both USDT and USDC Symbols

**File: `SignalBot/SignalBotRunner.cs`**

Enhance symbol caching to keep both USDT and USDC symbol sets:

```csharp
private HashSet<string>? _availableUsdcSymbols;  // existing
private HashSet<string>? _availableUsdtSymbols;  // new: for fallback validation

private async Task CacheAvailableSymbolsAsync(CancellationToken ct)
{
    var allSymbols = await _client.GetAllSymbolsAsync(ct);

    var executionSuffix = _settings.Trading.DefaultSymbolSuffix?.Trim().ToUpperInvariant() ?? "USDT";
    _availableUsdcSymbols = allSymbols
        .Where(s => s.EndsWith(executionSuffix, StringComparison.OrdinalIgnoreCase))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    if (_settings.Trading.EnableMultiAssetsFallback)
    {
        var signalSuffix = _settings.Trading.SignalSymbolSuffix?.Trim().ToUpperInvariant() ?? "USDT";
        _availableUsdtSymbols = allSymbols
            .Where(s => s.EndsWith(signalSuffix, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _logger.Information("Cached {UsdcCount} {Usdc} and {UsdtCount} {Usdt} futures symbols",
            _availableUsdcSymbols.Count, executionSuffix,
            _availableUsdtSymbols.Count, signalSuffix);
    }
}
```

#### Step 7: Track Fallback Positions for Monitoring

**File: `SignalBot/Models/SignalPosition.cs`**

Add tracking metadata to positions:
```csharp
public record SignalPosition
{
    // ... existing properties ...

    /// <summary>
    /// If true, this position was opened via Multi-Assets fallback
    /// and uses Cross Margin instead of Isolated.
    /// </summary>
    public bool IsMultiAssetsFallback { get; init; } = false;

    /// <summary>
    /// The originally preferred symbol (e.g., XYZUSDC) that was not available.
    /// </summary>
    public string? PreferredSymbol { get; init; }
}
```

#### Step 8: Startup Validation

**File: `SignalBot/SignalBotRunner.cs` - `StartAsync()`**

Add validation at startup when Multi-Assets fallback is enabled:
```csharp
if (_settings.Trading.EnableMultiAssetsFallback)
{
    _logger.Information("Multi-Assets Fallback is ENABLED. " +
        "Fallback positions will use Cross Margin instead of Isolated Margin.");

    // Check if Multi-Assets Mode is already enabled
    try
    {
        var isEnabled = await _client.GetMultiAssetsModeAsync(ct);
        _multiAssetsModeEnabled = isEnabled;
        _logger.Information("Multi-Assets Mode on Binance: {Status}",
            isEnabled ? "ENABLED" : "DISABLED (will enable on first fallback)");
    }
    catch (Exception ex)
    {
        _logger.Warning(ex, "Could not check Multi-Assets Mode status at startup");
    }
}
```

#### Step 9: Notifications Enhancement

Enhance telegram notifications to indicate fallback trades:
```csharp
// In NotifyPositionOpenedAsync or similar
if (position.IsMultiAssetsFallback)
{
    message += $"\n⚠️ Multi-Assets Fallback (Cross Margin)\n" +
               $"Preferred: {position.PreferredSymbol}\n" +
               $"Trading: {position.Symbol}";
}
```

#### Step 10: Unit Tests

Add tests for the new functionality:

1. **TestFallbackTriggeredWhenPreferredSymbolUnavailable** - Verify that when XYZUSDC is not available but XYZUSDT is, the bot falls back to XYZUSDT.
2. **TestFallbackDisabledByDefault** - Verify that with `EnableMultiAssetsFallback = false`, signals are skipped as before.
3. **TestFallbackSkipsWhenOriginalSymbolAlsoUnavailable** - Verify that when neither XYZUSDC nor XYZUSDT exist, the signal is skipped.
4. **TestMultiAssetsModeEnabledOnFirstFallback** - Verify Multi-Assets Mode is enabled on first fallback trade.
5. **TestCrossMarginSetForFallbackPositions** - Verify fallback positions use Cross Margin.
6. **TestIsolatedMarginStillUsedForNormalPositions** - Verify non-fallback positions still use Isolated Margin.

## 5. Risk Analysis

### 5.1 Cross Margin Risk

| Risk | Impact | Mitigation |
|------|--------|------------|
| Cross Margin shares balance across all positions | Losing positions can drain margin from winning ones | Enforce stricter position sizing for fallback trades |
| Cascading liquidations possible | One bad trade could liquidate multiple positions | Set `MaxMultiAssetsFallbackPositions` limit |
| PnL in USDT creates USDT balance that needs management | Auto-exchange may convert at unfavorable rates | Monitor USDT/USDC balance split |
| Cannot mix Isolated and Cross margin in Multi-Assets Mode | All positions become Cross Margin when mode is enabled globally | **This is the biggest risk** -- see Section 5.2 |

### 5.2 The Isolated vs Cross Margin Dilemma

**Critical issue**: Binance Multi-Assets Mode enables Cross Margin **globally for all symbols**. This means:

- If Multi-Assets Mode is enabled, **all existing Isolated Margin positions** would need to be switched to Cross Margin first
- New normal (USDC) positions would also use Cross Margin
- There is no way to have some positions in Isolated and some in Cross when Multi-Assets Mode is active

**Possible solutions:**

1. **Full Cross Margin Migration** (Simplest)
   - Switch the entire bot to Cross Margin + Multi-Assets Mode
   - Accept the higher risk profile
   - Compensate with stricter position sizing and max exposure limits
   - Pro: Simple implementation, no edge cases
   - Con: Higher risk for all positions

2. **On-Demand Toggle** (Moderate complexity)
   - Only enable Multi-Assets Mode when a fallback is needed and no Isolated positions exist
   - Disable it after the fallback position is closed
   - Pro: Isolated Margin preserved when possible
   - Con: Race conditions, complex state management, cannot use fallback if isolated positions are open

3. **Sub-Account Strategy** (Most complex, safest)
   - Use a Binance sub-account for Multi-Assets fallback trades
   - Main account stays in Isolated Margin for USDC trades
   - Sub-account runs in Cross Margin + Multi-Assets Mode for fallback trades
   - Pro: Complete risk isolation
   - Con: Requires sub-account API support, separate balance management, significant implementation effort

**Recommendation**: Start with **Option 1 (Full Cross Margin Migration)** as a configuration choice. Users who enable `EnableMultiAssetsFallback` accept that the bot switches to Cross Margin. Compensate with:
- Lower `MaxPositionPercent` (e.g., 15% instead of 25%)
- Lower `MaxTotalExposurePercent` (e.g., 50% instead of 80%)
- Add `MaxMultiAssetsFallbackPositions` to limit concurrent fallback positions

### 5.3 Auto-Exchange Risk

When the USDT balance goes below -5,000 USDT (for regular users), Binance auto-exchanges USDC -> USDT at the current market rate with a 0.01% haircut. This is generally acceptable but should be monitored.

### 5.4 PnL Tracking Complexity

Fallback positions generate PnL in USDT, while normal positions generate PnL in USDC. The bot's statistics tracking (`JsonTradeStatisticsStore`) would need to account for mixed-asset PnL. This could be handled by converting all PnL to a base asset (USDC or USD equivalent) for statistics.

## 6. Files to Modify

| File | Changes |
|------|---------|
| `SignalBot/Configuration/TradingSettings.cs` | Add `EnableMultiAssetsFallback` setting |
| `TradingBot.Binance/Futures/Interfaces/IBinanceFuturesClient.cs` | Add `GetMultiAssetsModeAsync`, `SetMultiAssetsModeAsync` |
| `TradingBot.Binance/Futures/BinanceFuturesClient.cs` | Implement Multi-Assets Mode methods |
| `TradingBot.Core/Models/TradingSignal.cs` | Add `IsMultiAssetsFallback` flag |
| `SignalBot/Models/SignalPosition.cs` | Add `IsMultiAssetsFallback`, `PreferredSymbol` |
| `SignalBot/SignalBotRunner.cs` | Fallback logic, symbol caching, Multi-Assets Mode lifecycle |
| `SignalBot/Services/Trading/SignalTrader.cs` | Dynamic margin type based on fallback flag |
| `SignalBot/Services/Trading/PositionManager.cs` | Handle fallback position metadata in notifications |
| `appsettings.json` | Add new configuration keys |
| `SignalBot.Tests/*` | Unit tests for fallback logic |

## 7. Implementation Order

| Phase | Task | Complexity |
|-------|------|------------|
| 1 | Add `GetMultiAssetsModeAsync` / `SetMultiAssetsModeAsync` to Binance client | Low |
| 2 | Add `EnableMultiAssetsFallback` configuration | Low |
| 3 | Add `IsMultiAssetsFallback` to `TradingSignal` and `SignalPosition` | Low |
| 4 | Cache both USDT and USDC symbols at startup | Low |
| 5 | Implement symbol fallback logic in `SignalBotRunner` | Medium |
| 6 | Dynamic margin type selection in `SignalTrader` | Low |
| 7 | Startup validation and Multi-Assets Mode lifecycle | Medium |
| 8 | Enhanced notifications for fallback trades | Low |
| 9 | Mixed-asset PnL tracking adjustments | Medium |
| 10 | Unit tests | Medium |

## 8. Testing Strategy

### 8.1 Testnet Validation

Binance Testnet supports USDS-M Futures and should support Multi-Assets Mode. Test the following scenarios on testnet:

1. Enable Multi-Assets Mode via API
2. Verify Cross Margin is enforced
3. Place a USDT-margined trade with USDC balance
4. Verify PnL denomination
5. Verify auto-exchange behavior
6. Disable Multi-Assets Mode and verify Isolated Margin works again

### 8.2 Scenarios to Test

| Scenario | Expected Result |
|----------|----------------|
| Signal for BTCUSDT, BTCUSDC exists | Trade BTCUSDC with Isolated Margin (no fallback) |
| Signal for XYZUSDT, XYZUSDC does NOT exist | Trade XYZUSDT with Cross Margin (fallback) |
| Signal for XYZUSDT, neither XYZUSDC nor XYZUSDT exist | Skip signal with notification |
| `EnableMultiAssetsFallback = false`, XYZUSDC missing | Skip signal (current behavior) |
| Multi-Assets Mode enable fails | Skip signal with error notification |
| Multiple fallback trades simultaneously | All use Cross Margin, shared collateral |

## 9. Summary

Multi-Assets Mode is a viable solution for the missing USDC symbol problem. The main trade-off is switching from Isolated to Cross Margin, which increases risk but enables broader symbol coverage. The implementation is moderate in complexity and can be rolled out behind a feature flag (`EnableMultiAssetsFallback = false` by default).

The recommended approach is to implement it as a configurable fallback mechanism that only activates when the preferred USDC symbol is unavailable, with clear notifications to the user about the margin type change.
