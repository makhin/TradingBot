# Cursor Project Rules (SignalBot)

These rules define how to work in this repository to keep changes safe, small, and aligned with the existing architecture.

## Project overview
- SignalBot is an automated trading bot that executes Telegram trading signals on Binance Futures.
- Tech: C# .NET 8.0 console app, event-driven pipeline, production + Docker.

## Scope allowlist (DO NOT go outside)
You may work ONLY in:
- `SignalBot/**`
- `SignalBot.Tests/**`
- `TradingBot.*/**` (shared libraries)
- `docs/**` (only if needed)

You MUST NOT read, reference, or modify:
- `ComplexBot/**`
- `ComplexBot.Tests/**`
- `ComplexBot.Integration/**`

If a request requires code outside the allowlist, stop and explain the limitation.

## Commands (preferred)
- Build: `dotnet build ../SignalBot.slnf`
- Run: `dotnet run ../SignalBot.slnf`
- Release build: `dotnet build ../SignalBot.slnf -c Release`
- Test: `dotnet test ../SignalBot.slnf`

Docker (repo root / SignalBot dir):
- Build image: `docker build -f SignalBot/SignalBot.Dockerfile -t signalbot .`
- Compose up: `docker-compose up -d`
- Logs: `docker-compose logs -f signalbot`
- Down: `docker-compose down`

## Architecture rules (event-driven pipeline)
Core processing flow:
Telegram Channel → TelegramSignalListener → SignalParser → SignalValidator
→ SignalTrader → OrderMonitor → PositionManager → JsonPositionStore

Key events:
- `OnSignalReceived` when a new Telegram signal arrives
- `OnTargetHit` when a take-profit target is reached
- `OnStopLossHit` when stop-loss is hit

When adding functionality:
- Prefer emitting/subscribing to events over direct calls.
- Keep coupling low.

## Domain model rules (immutability)
- Domain models are immutable C# records (often with `required` properties).
- Never mutate models in-place.
- Use `with` expressions to create new instances.

## Validation-first rule
- Signals must be validated before any execution.
- Validator may reject or adjust parameters (leverage caps, SL, liquidation safety, risk-reward).

## Binance / reliability rules
- All Binance API operations should use the existing Polly retry policies registered in `Program.cs`.
- When adding new Binance operations, wrap them using the registered retry policy.
- WebSocket monitoring is preferred (OrderMonitor implements `IOrderUpdateListener`).

## State persistence rules (JSON stores)
Storage patterns:
- `JsonFileStore<T>` for collections (positions, signals, trades)
- `JsonSingletonStore<T>` for singletons (bot state, statistics)

Rules:
- All stores must be thread-safe (SemaphoreSlim pattern already used).
- Persist position changes after every significant state change (entry, target hit, closure).
- Use `IPositionStore` abstractions; do not bypass the store with ad-hoc file I/O.
- Handle errors gracefully and log with context.

## Graceful shutdown
- Use `CancellationToken` throughout.
- Any long-running loop must respect cancellation tokens.

## Configuration rules
Hierarchy (lowest → highest):
1) `appsettings.json`
2) `.env` (DotNetEnv)
3) Environment variables (supports `TRADING_` prefix or nested keys like `SignalBot__Trading__MaxLeverage`)

Rules:
- Bind to strongly-typed settings classes in `Configuration/`.
- When adding options: update settings class + `appsettings.json`, and document if user-facing.

## Logging / observability
- Use Serilog structured logging with context (signal ID, position ID, symbol).
- Do not log secrets/PII.
- Keep logs useful for production troubleshooting.

## Telegram integration specifics
- Listener uses WTelegramClient (MTProto), not Bot API, for reading channels.
- Session persisted to `telegram_session.dat`.
- First run may require interactive auth (phone/code/2FA).

## Trading constraints
- Leverage is capped by risk override settings regardless of the signal.
- Skip trades if liquidation is too close to stop-loss (per validator logic).
- Cooldown system may reduce size after consecutive losses.
- Emergency mode can stop trading when drawdown threshold is exceeded.

## Testing rules
- If tests are added, they must go into a separate `SignalBot.Tests` project.
- Prefer deterministic tests (no real network, no timing flakiness).
- Follow existing conventions and Arrange-Act-Assert.

## Change discipline
- Default: do not change behavior unless explicitly requested.
- Prefer small, reviewable diffs.
- Keep public API stable unless asked to change it.
- Do not introduce new packages/dependencies unless explicitly asked.
- When making multi-file changes, list touched files and why.

## Output expectations (when proposing changes)
- Provide a concise plan.
- Provide minimal diffs.
- Call out risks (trading logic, money, state persistence) explicitly.
