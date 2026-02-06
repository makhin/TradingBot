# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SignalBot is an automated trading bot that executes Telegram trading signals on futures exchanges (Binance and Bybit). It's a C# .NET 8.0 console application with event-driven architecture, designed for production deployment with Docker support. The exchange can be switched via configuration without code changes.


# Focus: ComplexBot

## Scope (allowlist)
You are currently working ONLY in:
- `SignalBot/**`
- `SignalBot.Tests/**`
- `TradingBot.*/**` (shared libraries)
- `docs/**` (if needed)

Do NOT read or reference:
- `ComplexBot/**`
- `ComplexBot.Tests/**`
- `ComplexBot.Integration/**`

## Common Commands

### Build and Run
```bash
# Build the project
dotnet build ../SignalBot.slnf

# Run the application
dotnet run ../SignalBot.slnf

# Build with specific configuration
dotnet build ../SignalBot.slnf -c Release

# Test the project
dotnet test ../SignalBot.slnf
```

### Docker
```bash
# Build Docker image (from repo root)
docker build -f SignalBot/SignalBot.Dockerfile -t signalbot .

# Run with docker-compose (from SignalBot directory)
docker-compose up -d

# View logs
docker-compose logs -f signalbot

# Stop container
docker-compose down
```

### Testing
Currently no test project exists. When adding tests, they should be placed in a separate `SignalBot.Tests` project.

## Architecture

### Core Processing Flow
The application follows an event-driven pipeline:

```
Telegram Channel → TelegramSignalListener → SignalParser → SignalValidator
→ SignalTrader → OrderMonitor → PositionManager → JsonPositionStore
```

Key events:
- `OnSignalReceived` - Triggers when new signal arrives from Telegram
- `OnTargetHit` - Triggers when take-profit target is reached
- `OnStopLossHit` - Triggers when stop-loss is hit

### Project Dependencies
SignalBot depends on three sibling projects (multi-workspace setup):
- **TradingBot.Core** - Base models, interfaces, state abstractions, notifications, exchange abstraction layer
- **TradingBot.Binance** - Binance REST/WebSocket clients, order execution, adapters
- **TradingBot.Bybit** - Bybit REST/WebSocket clients, order execution, adapters

These are located in `../TradingBot.Core/`, `../TradingBot.Binance/`, and `../TradingBot.Bybit/` relative to SignalBot.

### Main Components

**SignalBotRunner** (`SignalBotRunner.cs`)
Main orchestrator that manages application lifecycle. Coordinates all services, subscribes to events, and handles graceful shutdown.

**TelegramSignalListener** (`Services/Telegram/TelegramSignalListener.cs`)
Connects to Telegram using WTelegramClient (MTProto). Listens to channels, emits `OnSignalReceived` event with raw message text.

**SignalParser** (`Services/Telegram/SignalParser.cs`)
Extracts trading signal data from Telegram messages using compiled regex patterns. Parses: symbol, direction (Long/Short), entry price, stop-loss, targets (1-4), leverage.

**SignalValidator** (`Services/Validation/SignalValidator.cs`)
Validates signals against risk parameters. Adjusts leverage (caps at `MaxLeverage`), calculates liquidation price, validates stop-loss placement, computes risk-reward ratio.

**SignalTrader** (`Services/Trading/SignalTrader.cs`)
Executes trades on the configured futures exchange (Binance or Bybit). Sets leverage/margin type, places market entry order, places stop-loss order, places take-profit orders (up to 4 targets). Uses Polly retry policies and exchange-agnostic interfaces.

**OrderMonitor** (`Services/Monitoring/OrderMonitor.cs`)
Monitors order fills via exchange WebSocket connections. Implements `IExchangeOrderUpdateListener` to receive real-time updates from the configured exchange. Emits `OnTargetHit` and `OnStopLossHit` events.

**PositionManager** (`Services/Trading/PositionManager.cs`)
Manages position lifecycle. Handles partial closes when targets hit, moves stop-loss to breakeven (configurable), calculates P&L, sends notifications.

**JsonFileStore<T>** (`State/JsonFileStore.cs`)
Generic base class for persisting collections of entities to JSON files. Provides thread-safe CRUD operations with SemaphoreSlim locking, JSON serialization with enum support, and error handling. Uses `Func<T, object>` key selector for upsert operations (Option A pattern). Supports filtering with predicates, batch updates, and atomic transformations.

**JsonSingletonStore<T>** (`State/JsonSingletonStore.cs`)
Generic base class for persisting single entities (not collections) to JSON files. Provides thread-safe Load/Save operations with locking. Used for configuration-like objects (bot state, statistics) where only one instance exists per file. Supports atomic updates via transformation functions.

**JsonPositionStore** (`State/JsonPositionStore.cs`)
Persists positions to JSON file (`signalbot_state.json`). Inherits from `JsonFileStore<SignalPosition>` and implements domain-specific filtering: `GetPositionBySymbolAsync` (filters by Symbol + Status), `GetOpenPositionsAsync` (filters by PositionStatus). Uses semaphore for thread-safe atomic operations.

**JsonTradeStatisticsStore** (`State/JsonTradeStatisticsStore.cs`)
Persists trade statistics to JSON file. Inherits from `JsonSingletonStore<TradeStatisticsState>`. Provides simple Load/Save operations for singleton statistics state.

**CooldownManager** (`Services/CooldownManager.cs`)
Implements loss-based cooldown logic. Tracks consecutive losses, triggers cooling periods, reduces position size after losses.

**BotController** (`Services/BotController.cs`)
Manages bot operating mode: Running, Paused, Stopped, EmergencyStopped. Controls whether new signals are accepted.

### Configuration System

Configuration hierarchy (lowest to highest priority):
1. `appsettings.json` - Default values
2. `.env` file - Environment-specific values (loaded via DotNetEnv)
3. Environment variables - Runtime overrides (use `TRADING_` prefix or nested keys like `SignalBot__Trading__MaxLeverage`)

All settings are bound to strongly-typed classes in the `Configuration/` folder. Key sections:
- `SignalBotSettings` - Root container
- `ExchangeSettings` - Exchange selection and credentials (Binance, Bybit)
- `TelegramSettings` - API credentials, channels, session path
- `TradingSettings` - Entry mode, targets, position management
- `RiskOverrideSettings` - Leverage caps, liquidation safety, max drawdown
- `PositionSizingSettings` - Position size calculation modes
- `CooldownSettings` - Loss-based cooldown durations
- `NotificationSettings` - Telegram bot credentials for alerts

### Data Models

**TradingSignal** (`Models/TradingSignal.cs`)
Immutable record representing parsed and validated signal. Contains both original values (from signal) and adjusted values (after validation). Key fields: Symbol, Direction, Entry, StopLoss, Targets, Leverage, LiquidationPrice, RiskRewardRatio.

**SignalPosition** (`Models/SignalPosition.cs`)
Immutable record representing active or closed position. Tracks: OrderIds (entry, SL, TPs), Quantities (initial/remaining), Status (Pending/Open/PartialClose/Closed), P&L (realized/unrealized), Targets (with hit status).

**Enums:**
- `SignalDirection` - Long, Short
- `PositionStatus` - Pending, Open, PartialClose, Closed
- `PositionCloseReason` - TargetHit, StopLossHit, ManualClose, Liquidation
- `BotOperatingMode` - Running, Paused, Stopped, EmergencyStopped

### Dependency Injection

`Program.cs` sets up the DI container. All services are registered as singletons (stateful services like OrderMonitor, PositionManager) or transient. Key registrations:
- Exchange clients (Binance and Bybit REST/WebSocket clients)
- Exchange factory (`IExchangeFactory`) for creating exchange-specific implementations
- Active exchange interfaces resolved via factory based on configuration:
  - `IFuturesExchangeClient` - Market data and account operations
  - `IFuturesOrderExecutor` - Order placement and management
  - `IExchangeOrderUpdateListener` - Real-time order updates
  - `IExchangeKlineListener` - Real-time price data
- All SignalBot services with their interfaces
- Polly retry policies (3 retries with exponential backoff)
- Serilog logger with multiple sinks (Console, File, Elasticsearch, Loki, Seq)

### Signal Format

Expected format (parsed by SignalParser):
```
#SYMBOL/USDT - Long🟢 | Short🔴

Entry: X.XXXX
Stop Loss: X.XXXX

Target 1: X.XXXX
Target 2: X.XXXX
Target 3: X.XXXX
Target 4: X.XXXX

Leverage: xNN
```

Parser uses `[GeneratedRegex]` attribute for compiled regex performance. Emoji indicators (🟢/🔴) are optional but supported.

## Key Development Patterns

### Event-Driven Architecture
The bot is built around events. When adding new functionality, prefer emitting events over direct method calls to maintain loose coupling.

### Immutable Records
Domain models use C# records with `required` properties. When modifying state, use `with` expressions to create new instances rather than mutating.

### Retry Policies
All exchange API calls use Polly retry policies (configured in Program.cs). When adding new API operations, wrap them with the registered retry policy. The retry policy is configured for `ExecutionResult` (Core model).

### Validation First
Signals go through validation before execution. The validator can reject signals or adjust parameters (leverage, SL). Risk validation is separate from parsing.

### State Persistence
The project uses a generic JSON persistence layer with two base classes:
- **JsonFileStore<T>** - For collections (positions, signals, trades). Thread-safe CRUD with key selectors.
- **JsonSingletonStore<T>** - For single objects (bot state, statistics). Thread-safe Load/Save operations.

Positions are persisted to disk after every significant change (entry, target hit, closure). The `IPositionStore` abstraction allows swapping storage backends. All stores use SemaphoreSlim for thread safety and handle errors gracefully.

### Graceful Shutdown
The application uses `CancellationToken` throughout. When adding long-running operations, respect cancellation tokens to enable graceful shutdown.

## File Organization

```
SignalBot/
├── Program.cs                    # DI setup, startup logic
├── SignalBotRunner.cs           # Main orchestrator
├── Configuration/               # 12 strongly-typed settings classes
├── Models/                      # Domain models (signals, positions)
├── Services/
│   ├── Telegram/               # Signal listening & parsing
│   ├── Trading/                # Signal execution & position management
│   ├── Validation/             # Risk validation
│   ├── Monitoring/             # WebSocket order monitoring
│   ├── Commands/               # Telegram bot command handlers
│   ├── BotController.cs        # Operating mode management
│   └── CooldownManager.cs      # Loss-based cooldown logic
├── State/                       # Persistence layer
│   ├── JsonFileStore.cs        # Generic collection store base class
│   ├── JsonSingletonStore.cs   # Generic singleton store base class
│   ├── JsonPositionStore.cs    # Position persistence (uses JsonFileStore)
│   └── JsonTradeStatisticsStore.cs  # Statistics persistence (uses JsonSingletonStore)
├── Telemetry/                   # OpenTelemetry setup
└── appsettings.json            # Configuration

Related Projects (sibling directories):
../TradingBot.Core/             # Base interfaces, models, notifications, exchange abstractions
../TradingBot.Binance/          # Binance API clients, order execution, adapters
../TradingBot.Bybit/            # Bybit API clients, order execution, adapters
```

## Important Implementation Notes

### Telegram Integration
- Uses **WTelegramClient** (MTProto) for listening to channels, not Telegram Bot API
- Session data saved to `telegram_session.dat` (persistent authentication)
- On first run, requires interactive authentication (phone number, code, optional 2FA)
- Can listen to multiple channels simultaneously (configured in `TelegramSettings.ChannelIds`)

### Exchange Support
**Multi-Exchange Architecture:**
- Supports Binance Futures and Bybit Futures (USDT perpetual contracts)
- Exchange selection via configuration: `Exchange.ActiveExchange` (set to "Binance" or "Bybit")
- Each exchange has its own API credentials and testnet/mainnet settings
- Exchange-agnostic business logic using adapter pattern
- Runtime switching without code changes

**Binance Futures:**
- Testnet and Mainnet support (configured via `Exchange.Binance.UseTestnet`)
- Uses Isolated or Cross margin (configurable per-position)
- Supports OneWay and Hedge position modes
- All orders placed with `newClientOrderId` for tracking
- WebSocket subscription for real-time order updates

**Bybit Futures:**
- Testnet and Mainnet support (configured via `Exchange.Bybit.UseTestnet`)
- USDT-M perpetual contracts (Category.Linear)
- Unified margin trading system
- Note: WebSocket subscriptions are stub implementations (requires Bybit.Net API investigation)

### Risk Management
- Leverage is capped at `RiskOverrideSettings.MaxLeverage` regardless of signal value
- Liquidation price calculated before entry; position skipped if too close to SL
- Position size calculation supports multiple modes (FixedAmount, RiskPercent, FixedMargin)
- Cooldown system reduces position size after consecutive losses
- Emergency circuit breaker can stop all trading if drawdown threshold exceeded

### Logging & Observability
- Structured logging with Serilog (JSON format for machine parsing)
- Logs written to `logs/signalbot-YYYYMMDD.txt` (daily rolling)
- Optional sinks: Elasticsearch, Grafana Loki, Seq
- OpenTelemetry distributed tracing (OTLP exporter)
- All significant operations logged with context (signal ID, position ID, symbol)

### Docker Deployment
- Multi-stage Dockerfile: SDK build → Runtime image
- Supports ARM64 (Raspberry Pi 4) and x64 architectures
- Runs as non-root user (`signalbot:signalbot`)
- Persistent volumes for: state, Telegram session, logs
- Healthcheck: Verifies process is running
- Memory limits: 1GB max, 256MB reservation

## When Making Changes

### Adding a New Service
1. Create interface in `Services/` subdirectory
2. Implement interface with dependency injection constructor
3. Register in `Program.cs` DI container
4. Inject into `SignalBotRunner` if it needs to be started/stopped
5. Subscribe to events if the service reacts to signals/orders

### Modifying Signal Parser
- Update regex patterns in `SignalParser.cs`
- Test against real signal examples from Telegram
- Consider backward compatibility (old signal formats)
- Update `TradingSignal` model if adding new fields

### Adding Configuration Options
1. Add property to appropriate settings class in `Configuration/`
2. Add default value to `appsettings.json`
3. Document in README.md if user-facing
4. Settings are automatically bound via `services.Configure<T>(config)`

**Exchange Configuration:**
To switch between exchanges, update `appsettings.json`:
```json
{
  "SignalBot": {
    "Exchange": {
      "ActiveExchange": "Binance",  // or "Bybit"
      "Binance": {
        "UseTestnet": true,
        "ApiKey": "your-key",
        "ApiSecret": "your-secret"
      },
      "Bybit": {
        "UseTestnet": true,
        "ApiKey": "your-key",
        "ApiSecret": "your-secret"
      }
    }
  }
}
```

### Changing Order Execution Logic
- Modify `SignalTrader.cs` or `PositionManager.cs`
- Ensure retry policies are applied (`_retryPolicy.ExecuteAsync()`)
- Update position state in store after changes
- Emit appropriate events for monitoring
- Send notifications for user-visible changes

### Working with Position State
- Never mutate `SignalPosition` directly (it's immutable)
- Use `with` expressions to create modified copies
- Save to store immediately after changes: `await _positionStore.SavePositionAsync(position)`
- Use position.Id as correlation identifier in logs

### Adding New Persistence Stores
When adding new persisted entities, choose the appropriate base class:

**For collections** (list of entities):
```csharp
public class JsonSignalHistoryStore : JsonFileStore<TradingSignal>
{
    public JsonSignalHistoryStore(string filePath) : base(filePath) { }

    // Add domain-specific queries
    public async Task<List<TradingSignal>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        var signals = await LoadAllAsync(ct);
        return signals.OrderByDescending(s => s.Timestamp).Take(count).ToList();
    }
}
```

**For singleton objects** (one instance per file):
```csharp
public class JsonBotStateStore : JsonSingletonStore<SignalBotState>
{
    public JsonBotStateStore(string filePath) : base(filePath) { }

    // Inherits Load/Save methods automatically
}
```

Key points:
- Use `AddOrUpdateAsync(entity, keySelector)` for upsert operations in collections
- Use `LoadAllAsync()` for reading without locks (safe for concurrent reads)
- Use `DeleteAsync(predicate)` for removing entities
- Both base classes handle directory creation, error logging, and JSON serialization automatically

## Additional Resources

- Full design document: `docs/SIGNALBOT_DESIGN.md`
- Configuration schema: `appsettings.json` (all options documented inline)
- Related projects:
  - `../TradingBot.Core/` - Exchange-agnostic interfaces and models
  - `../TradingBot.Binance/` - Binance implementation and adapters
  - `../TradingBot.Bybit/` - Bybit implementation and adapters (WebSocket stubs)
