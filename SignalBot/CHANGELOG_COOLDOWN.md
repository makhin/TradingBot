# Cooldown System Implementation

## Summary

Implemented comprehensive cooldown system to prevent revenge trading after losses. The system automatically pauses trading after stop-losses or liquidations, progressively reduces position sizes, and tracks consecutive wins/losses.

## Features Implemented

### 1. Core Components

#### CooldownSettings ([Configuration/CooldownSettings.cs](Configuration/CooldownSettings.cs))
Configuration model with 11 parameters:
- `Enabled` - Master switch for cooldown system
- `CooldownAfterStopLoss` - Pause duration after each stop-loss (default: 15 minutes)
- `CooldownAfterLiquidation` - Pause duration after liquidation (default: 1 hour)
- `ConsecutiveLossesForLongCooldown` - Number of losses to trigger extended cooldown (default: 3)
- `LongCooldownDuration` - Extended pause after series of losses (default: 2 hours)
- `ReduceSizeAfterLosses` - Enable position size reduction
- `SizeMultiplierAfter1Loss` - Size multiplier after 1 loss (default: 0.75 = 75%)
- `SizeMultiplierAfter2Losses` - Size multiplier after 2 losses (default: 0.5 = 50%)
- `SizeMultiplierAfter3PlusLosses` - Size multiplier after 3+ losses (default: 0.25 = 25%)
- `WinsToResetLossCounter` - Consecutive wins needed to reset loss counter (default: 2)

#### CooldownStatus ([Models/CooldownStatus.cs](Models/CooldownStatus.cs))
Read-only status model containing cooldown state information.

#### CooldownManager ([Services/CooldownManager.cs](Services/CooldownManager.cs))
Main service managing cooldown logic with thread-safe state management.

### 2. Bot Commands Integration

**Updated /status command:**
Shows cooldown status when active with remaining time and consecutive losses.

**New /resetcooldown command:**
Manually override and clear active cooldown period.

## Test Coverage

Created CooldownManagerTests.cs with **13 comprehensive tests** (all passing ✅).

Total tests: 28 (15 existing + 13 new)
All passed in 275 ms

## Files Modified/Created

**New Files:**
- SignalBot/Models/CooldownStatus.cs
- SignalBot/Services/CooldownManager.cs
- SignalBot.Tests/CooldownManagerTests.cs
- SignalBot/CHANGELOG_COOLDOWN.md

**Modified Files:**
- SignalBot/Configuration/CooldownSettings.cs (converted to record type)
- SignalBot/appsettings.json (added WinsToResetLossCounter)
- SignalBot/Program.cs (registered CooldownManager in DI)
- SignalBot/SignalBotRunner.cs (integrated cooldown checks and position close events)
- SignalBot/Services/Commands/IBotCommands.cs (added ResetCooldownAsync)
- SignalBot/Services/Commands/TelegramBotCommands.cs (implemented cooldown commands)
- SignalBot/Services/Commands/TelegramCommandHandler.cs (added /resetcooldown route)
- SignalBot.Tests/BotCommandsTests.cs (updated constructor)

---

**Implementation completed:** Full cooldown system operational with comprehensive test coverage.
