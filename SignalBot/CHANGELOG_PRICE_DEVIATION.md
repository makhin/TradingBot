# Price Deviation Check Implementation

## Summary

Implemented price deviation check logic to prevent SignalTrader from executing trades when market price has moved significantly from signal entry price.

## Changes Made

### 1. Added Enum Types ([SignalBot/Models/](SignalBot/Models/))

**PriceDeviationAction.cs** - Defines actions when price deviates:
- `Skip` - Skip signal entirely
- `EnterAtMarket` - Enter despite deviation
- `PlaceLimitAtEntry` - Place limit order at signal entry (not yet implemented)
- `EnterAndAdjustTargets` - Enter at market and adjust targets proportionally

**LimitOrderPricing.cs** - Limit order pricing strategy:
- `AtEntry` - Place at signal entry price
- `AtCurrentPrice` - Place at current market price
- `MidPoint` - Place between entry and current price

### 2. Updated Configuration

**EntrySettings.cs**:
- Changed `DeviationAction` from `string` to `PriceDeviationAction` enum
- Added `LimitPricing` field with `LimitOrderPricing` enum type

**appsettings.json**:
- Added `"LimitPricing": "AtEntry"` to Entry section

### 3. Enhanced SignalTrader

**SignalTrader.cs** - Added price deviation logic:

```csharp
// Check current market price vs signal entry
var currentPrice = await _client.GetMarkPriceAsync(signal.Symbol, ct);
var deviationPercent = Math.Abs(currentPrice - signal.Entry) / signal.Entry * 100;

if (deviationPercent > _entrySettings.MaxPriceDeviationPercent)
{
    switch (_entrySettings.DeviationAction)
    {
        case PriceDeviationAction.Skip:
            // Cancel position and throw exception
        case PriceDeviationAction.EnterAtMarket:
            // Continue with execution
        case PriceDeviationAction.EnterAndAdjustTargets:
            // Adjust targets and enter
    }
}
```

**AdjustTargetsForPriceDeviation()** method:
- Calculates price shift between signal entry and actual entry
- Shifts all target prices by the same amount
- Logs original and adjusted targets for transparency

### 4. Updated Dependency Injection

**Program.cs**:
- Added `services.AddSingleton(signalBotSettings.Entry);` to register EntrySettings

### 5. Added Unit Tests

**SignalBot.Tests/PriceDeviationTests.cs** - 3 test cases:

1. **ExecuteSignal_PriceWithinDeviation_ShouldExecute**
   - Price deviates 0.3% (within 0.5% limit)
   - Should execute trade normally

2. **ExecuteSignal_PriceExceedsDeviation_Skip_ShouldCancel**
   - Price deviates 1.5% (exceeds 0.5% limit)
   - DeviationAction = Skip
   - Should cancel position and throw exception

3. **ExecuteSignal_PriceExceedsDeviation_AdjustTargets_ShouldAdjust**
   - Price deviates 1.0% (exceeds 0.5% limit)
   - DeviationAction = EnterAndAdjustTargets
   - Should enter at market and shift targets proportionally

## Test Results

```
Test Run Successful.
Total tests: 3
     Passed: 3
```

## Configuration Example

```json
{
  "SignalBot": {
    "Entry": {
      "MaxPriceDeviationPercent": 0.5,
      "DeviationAction": "Skip",
      "UseLimitOrder": false,
      "LimitPricing": "AtEntry",
      "LimitOrderTtl": "00:05:00",
      "MaxSlippagePercent": 0.3
    }
  }
}
```

## Usage Scenarios

### Scenario 1: Conservative (Skip on deviation)
```json
"DeviationAction": "Skip",
"MaxPriceDeviationPercent": 0.5
```
- Signal entry: $100
- Current price: $101.5 (+1.5%)
- **Result**: Signal cancelled, position not opened

### Scenario 2: Aggressive (Enter anyway)
```json
"DeviationAction": "EnterAtMarket",
"MaxPriceDeviationPercent": 0.5
```
- Signal entry: $100
- Current price: $101.5 (+1.5%)
- **Result**: Enter at $101.5, keep original targets

### Scenario 3: Adaptive (Adjust targets)
```json
"DeviationAction": "EnterAndAdjustTargets",
"MaxPriceDeviationPercent": 0.5
```
- Signal entry: $100, targets: [101, 102, 103, 104]
- Current price: $101 (+1%)
- **Result**: Enter at $101, adjusted targets: [102, 103, 104, 105]

## Breaking Changes

None - backward compatible via default enum values.

## Future Work

- Implement `PlaceLimitAtEntry` action for limit order placement
- Add option to preserve R:R ratio when adjusting targets instead of simple shift
- Add metrics for tracking how often signals are skipped due to deviation

## Files Modified

- SignalBot/Models/PriceDeviationAction.cs (new)
- SignalBot/Models/LimitOrderPricing.cs (new)
- SignalBot/Configuration/EntrySettings.cs
- SignalBot/Services/Trading/SignalTrader.cs
- SignalBot/Program.cs
- SignalBot/appsettings.json
- SignalBot.Tests/PriceDeviationTests.cs (new)

## Commit

This implementation resolves the critical issue identified in the design document review where SignalTrader was executing trades without checking if the current market price had deviated significantly from the signal entry price.
