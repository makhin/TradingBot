# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a .NET 8 cryptocurrency trading bot for Binance implementing multiple trend-following strategies with comprehensive backtesting, optimization, and risk management. The primary strategy is ADX Trend Following with Volume Confirmation, targeting Sharpe Ratio 1.5-1.9 and Max Drawdown <20%.

## Build and Run Commands

### Main Application
```bash
cd ComplexBot
dotnet restore
dotnet build
dotnet run
```

### Unit Tests
```bash
cd ComplexBot.Tests
dotnet test                                                    # Run all tests
dotnet test --filter "ClassName=ComplexBot.Tests.RiskManagerTests"  # Run specific test class
dotnet test -v detailed                                        # Verbose output
dotnet test /p:CollectCoverage=true                           # With coverage
```

### Integration Tests
```bash
cd ComplexBot.Integration
dotnet test --filter "ConfigurationIntegrationTests"          # Always work, no API keys needed
dotnet test --filter "BinanceLiveTraderIntegrationTests"      # Requires Binance Testnet API keys
dotnet test --filter "Name~GetAccountBalance"                 # Run specific test by name
dotnet test -v detailed                                        # Verbose output
```

## Architecture Overview

### Runner-Based Execution Model
The application uses a **dispatcher-runner pattern** where `Program.cs` instantiates specialized runner classes, each responsible for a distinct execution mode. This avoids monolithic switch statements and keeps concerns separated.

**Key runners (in `ComplexBot/`):**
- `BacktestRunner` - Executes backtests on historical data
- `OptimizationRunner` - Parameter optimization (grid search, genetic algorithms)
- `AnalysisRunner` - Walk-forward analysis and Monte Carlo simulation
- `LiveTradingRunner` - Paper/live trading on Binance
- `DataRunner` - Downloads historical data from Binance

**Entry flow:**
1. `Program.Main()` creates all runners with dependencies
2. `AppMenu.PromptMode()` shows interactive menu
3. `ModeDispatcher.DispatchAsync(mode)` routes to appropriate runner

### Strategy Architecture
All strategies inherit from `StrategyBase<TSettings>` ([ComplexBot/Services/Strategies/StrategyBase.cs](ComplexBot/Services/Strategies/StrategyBase.cs)), which implements the Template Method pattern:

```csharp
public abstract class StrategyBase<TSettings> : IStrategy
{
    // Template method - calls hooks in specific order
    public TradeSignal? Analyze(Candle candle, decimal? currentPosition, string symbol)

    // Hooks for derived strategies to implement
    protected abstract void UpdateIndicators(Candle candle);
    protected abstract TradeSignal? CheckEntryConditions(...);
    protected abstract TradeSignal? CheckExitConditions(...);
}
```

**Available strategies:**
- `AdxTrendStrategy` - Main strategy (ADX + EMA + Volume + OBV)
- `MaStrategy` - Moving average crossover
- `RsiStrategy` - RSI mean reversion
- `StrategyEnsemble` - Combines multiple strategies with weighted voting

**Creating a new strategy:**
1. Inherit from `StrategyBase<YourSettings>`
2. Define settings class in `Services/Strategies/YourStrategySettings.cs`
3. Implement abstract methods: `UpdateIndicators`, `CheckEntryConditions`, `CheckExitConditions`
4. Register in `StrategyFactory.CreateStrategy()`
5. Add configuration mapping in `BotConfiguration.cs`

### Configuration System
Uses a layered configuration approach:
1. **BotConfiguration** ([ComplexBot/Configuration/BotConfiguration.cs](ComplexBot/Configuration/BotConfiguration.cs)) - Top-level config loaded from `appsettings.json`
2. **ConfigSettings classes** - JSON-friendly configuration (e.g., `StrategyConfigSettings`)
3. **Settings classes** - Domain models used by services (e.g., `StrategySettings`)
4. **Conversion methods** - `ToStrategySettings()`, `ToRiskSettings()`, etc.

This separation allows easy JSON serialization while keeping domain logic clean.

**Key configuration sections:**
- `BinanceApi` - API credentials, testnet toggle
- `Strategy` - ADX strategy parameters (thresholds, periods, etc.)
- `RiskManagement` - Position sizing, drawdown limits
- `Backtesting` - Initial capital, commissions, slippage
- `LiveTrading` - Symbol, interval, paper trading mode
- `Optimization` - Parameter ranges for grid/genetic search

**Environment Variables (.env file):**
The application loads configuration from a `.env` file using DotNetEnv. This allows you to keep sensitive credentials outside of `appsettings.json`.

Create a `.env` file in the `ComplexBot/` directory with the following format:
```bash
# Binance Testnet Credentials (for development/testing)
BINANCE_TESTNET_KEY=your-testnet-api-key
BINANCE_TESTNET_SECRET=your-testnet-secret

# Binance Mainnet Credentials (for live trading)
BINANCE_MAINNET_KEY=your-mainnet-api-key
BINANCE_MAINNET_SECRET=your-mainnet-secret

# Trading Configuration
TRADING_BinanceApi__UseTestnet=true

# Telegram Notifications (Optional)
TELEGRAM_BOT_TOKEN=your-telegram-bot-token
TELEGRAM_CHAT_ID=your-chat-id
```

**How it works:**
1. [ConfigurationService.cs](ComplexBot/Configuration/ConfigurationService.cs) loads the `.env` file at startup
2. Based on `TRADING_BinanceApi__UseTestnet`, it selects testnet or mainnet credentials
3. Environment variables override `appsettings.json` values
4. Supports both formats: `BINANCE_TESTNET_KEY` (preferred) and `BINANCE_API_KEY` (fallback)

**Security:** Always add `.env` to `.gitignore` to prevent committing secrets to version control.

### Backtesting Engine
The backtesting system ([ComplexBot/Services/Backtesting/](ComplexBot/Services/Backtesting/)) supports:
- **BacktestEngine** - Core simulation loop with position tracking
- **WalkForwardAnalyzer** - Out-of-sample validation, calculates WFE (Walk-Forward Efficiency)
- **MonteCarloSimulator** - Risk analysis via trade sequence randomization
- **ParameterOptimizer** - Grid search across parameter combinations
- **GeneticOptimizer** - Evolutionary optimization for large search spaces

**Walk-Forward Analysis Configuration:**
The analyzer is configurable via `WalkForwardSettings`:
- `InSampleRatio` (default 0.7) - Portion of data for optimization/training (70%)
- `OutOfSampleRatio` (default 0.2) - Portion for validation/testing (20%)
- `StepRatio` (default 0.1) - How much to advance between periods (10%)
- `MinWfeThreshold` (default 50%) - Minimum WFE to consider strategy robust
- `MinConsistencyThreshold` (default 60%) - Minimum % of profitable OOS periods
- `MinSharpeThreshold` (default 0.5) - Minimum acceptable Sharpe ratio

**Example:** With 1000 candles and default settings:
- IS window: 700 candles, OOS window: 200 candles
- Step: 100 candles → generates ~2 periods
- To get more periods: reduce `StepRatio` (e.g., 0.05 = 50 candles/step → ~4 periods)

Users can customize these via interactive prompts when running Walk-Forward Analysis. The system calculates and displays the estimated number of periods before execution.

**Optimization targets:**
- `RiskAdjusted` - Sharpe * (1 - drawdown/100)
- `SharpeRatio` - Return per unit of volatility
- `SortinoRatio` - Return per unit of downside volatility
- `ProfitFactor` - Gross profit / gross loss
- `TotalReturn` - Absolute percentage return

### Risk Management
Multi-layered risk system in [ComplexBot/Services/RiskManagement/](ComplexBot/Services/RiskManagement/):
- **RiskManager** - Position sizing based on ATR, applies drawdown-based risk reduction
- **PortfolioRiskManager** - Multi-asset correlation tracking, concurrent position limits
- **TradeJournal** - Records all trades with MAE/MFE metrics for post-analysis

**Jerry Parker Drawdown Rule:** When drawdown exceeds thresholds (10%/15%/20%), risk per trade is progressively reduced (0.75x/0.5x/0.25x).

### Data Flow
Historical data loading ([HistoricalDataLoader.cs](ComplexBot/Services/Backtesting/HistoricalDataLoader.cs)):
1. Fetches klines from Binance API (spot or futures)
2. Converts to internal `Candle` model
3. Caches to CSV files in `HistoricalData/{symbol}/{interval}/`
4. Supports automatic updates for existing data

Live trading data flow:
1. `BinanceLiveTrader` subscribes to kline WebSocket
2. On each closed candle, calls `strategy.Analyze()`
3. Executes trades via REST API (market orders + OCO for stop/target)
4. Paper mode simulates fills without API calls

## Important Patterns and Conventions

### User Input with SpectreHelpers
**IMPORTANT**: Always use [SpectreHelpers](ComplexBot/Utils/SpectreHelpers.cs) for numeric user input to prevent locale-specific formatting issues.

**Problem**: Russian (and other) locales use commas as decimal separators (e.g., `10000,5`), which Spectre.Console interprets as color codes, causing crashes like:
```
System.InvalidOperationException: Could not find color or style '10000,0'
```

**Solution**: Use `SpectreHelpers.AskDecimal()` and `SpectreHelpers.AskInt()` instead of `AnsiConsole.Ask()` for numbers.

**Example:**
```csharp
using ComplexBot.Utils;

// ❌ BAD - Will crash with Russian locale
var capital = AnsiConsole.Ask("Initial capital [green](USDT)[/]:", 10000m);

// ✅ GOOD - Locale-safe with validation
var capital = SpectreHelpers.AskDecimal("Initial capital [green](USDT)[/]", 10000m, min: 1m, max: 1000000m);
var simulations = SpectreHelpers.AskInt("Number of simulations", 1000, min: 100, max: 10000);
```

**Features:**
- Uses `InvariantCulture` for formatting to avoid locale issues
- Escapes brackets `[[...]]` to prevent Spectre.Console markup conflicts
- Supports both comma and dot as decimal separators in input
- Optional min/max validation
- Returns validated numeric values

### Strategy Selection in Runners
Recent refactoring (commits #16-17) added strategy selection to all analysis modes. When working with runners:
- Use `StrategyFactory.CreateStrategy(strategyName)` for instantiation
- Prompt user for strategy choice in interactive menus
- Pass strategy settings from configuration, not hardcoded defaults

### Non-Interactive Mode for Docker
The application supports running in non-interactive mode (e.g., in Docker containers) via the `TRADING_MODE` environment variable.

**How it works:**
1. [Program.cs](ComplexBot/Program.cs:32-59) checks for `TRADING_MODE` environment variable
2. If set, skips interactive menu and auto-starts the specified mode
3. Runners check `AnsiConsole.Profile.Capabilities.Interactive` to determine if terminal is interactive
4. In non-interactive mode, configuration values from `appsettings.json` are used automatically

**Available TRADING_MODE values:**
- `live` - Paper trading (testnet)
- `live-real` - Real trading (requires `CONFIRM_LIVE_TRADING=yes`)
- `backtest` - Backtesting
- `optimize` - Parameter optimization
- `walkforward` - Walk-forward analysis
- `montecarlo` - Monte Carlo simulation
- `download` - Download historical data

**Example:**
```bash
docker compose run --rm -e TRADING_MODE=live tradingbot
```

**When adding new interactive prompts:**
Always check `AnsiConsole.Profile.Capabilities.Interactive` before showing prompts. In non-interactive mode, use configuration values or environment variables instead.

### Test Data Generation
Unit tests use helper methods to generate synthetic candles:
```csharp
GenerateUptrendCandles(int count)     // 2% growth per candle
GenerateDowntrendCandles(int count)   // 2% decline per candle
GenerateRangingCandles(int count)     // Sideways movement
```

See [ComplexBot.Tests/AdxTrendStrategyTests.cs](ComplexBot.Tests/AdxTrendStrategyTests.cs) for examples.

### Indicator Calculation
Indicators ([ComplexBot/Services/Indicators/Indicators.cs](ComplexBot/Services/Indicators/Indicators.cs)) use list-based state:
- Maintain buffers (e.g., `List<decimal> emaValues`)
- Check `IsReady` before using values
- Call `Update(candle)` sequentially in chronological order

**Do not** reset indicators mid-backtest unless explicitly testing reset behavior.

### Trading Modes
The bot supports multiple execution contexts:
- **Backtest** - Historical simulation, no API calls
- **Paper (Testnet)** - Live data, simulated fills, uses Binance testnet
- **Live (Testnet)** - Real orders on testnet with fake money
- **Live (Mainnet)** - Real orders with real money (use `UseTestnet: false`)

Always verify `UseTestnet` and `PaperTrade` flags in `appsettings.json` before running.

## Working with the Codebase

### Adding a New Indicator
1. Add calculation method to [Indicators.cs](ComplexBot/Services/Indicators/Indicators.cs)
2. Add indicator state to relevant strategy class
3. Update `UpdateIndicators()` method in strategy
4. Add unit tests in [IndicatorsTests.cs](ComplexBot.Tests/IndicatorsTests.cs)

### Adding a New Optimizer
1. Create class in `Services/Backtesting/` (e.g., `YourStrategyOptimizer.cs`)
2. Inherit from base optimizer pattern or implement custom
3. Define parameter ranges in `BotConfiguration.cs`
4. Add optimizer selection in `OptimizationRunner.cs`
5. Add configuration section to `appsettings.json`

### Modifying Risk Rules
Risk logic is centralized in [RiskManager.cs](ComplexBot/Services/RiskManagement/RiskManager.cs):
- Position sizing: `CalculatePositionSize()`
- Drawdown adjustments: `GetDrawdownAdjustedRisk()`
- Daily limits: `IsDailyLimitExceeded()`

Changes to risk rules should be accompanied by tests in [RiskManagerTests.cs](ComplexBot.Tests/RiskManagerTests.cs).

## Testing Strategy

### Current Test Coverage
- Unit tests: 39 tests covering indicators, strategies, risk management, backtesting
- Integration tests: Configuration validation + Binance API tests (skipped by default)

### Running Testnet Integration Tests
1. Get API keys from https://testnet.binance.vision/
2. Set environment variables:
   ```bash
   export TRADING_BinanceApi__ApiKey="your-testnet-key"
   export TRADING_BinanceApi__ApiSecret="your-testnet-secret"
   export TRADING_BinanceApi__UseTestnet="true"
   ```
3. Remove `[Fact(Skip = "...")]` from tests in [BinanceLiveTraderIntegrationTests.cs](ComplexBot.Integration/BinanceLiveTraderIntegrationTests.cs)
4. Run: `dotnet test ComplexBot.Integration`

**Never commit real API keys to version control.**

## Performance Metrics
After backtest, validate strategy with:
- **Sharpe Ratio** > 1.5 (good), > 1.0 (acceptable)
- **Max Drawdown** < 20%
- **Profit Factor** > 1.5
- **Win Rate** > 40% (with good avg win/loss ratio)
- **Walk-Forward Efficiency (WFE)** > 50%
- **Monte Carlo Ruin Probability** < 5%

These thresholds are based on research targets documented in [ComplexBot/README.md](ComplexBot/README.md).

## Key Dependencies
- **Binance.Net** 10.3.0 - Binance API client
- **CryptoExchange.Net** 8.3.0 - Base exchange infrastructure
- **MathNet.Numerics** 5.0.0 - Statistical calculations
- **Spectre.Console** 0.49.1 - CLI rendering
- **xUnit** - Testing framework (with Moq for mocking)

## Solution Structure
```
TradingBot.sln
├── ComplexBot/              # Main application
│   ├── Models/              # Domain models (Candle, Trade, Signal)
│   ├── Services/
│   │   ├── Indicators/      # Technical indicators (EMA, ATR, ADX, etc.)
│   │   ├── Strategies/      # Trading strategies
│   │   ├── RiskManagement/  # Position sizing, risk rules
│   │   ├── Backtesting/     # Simulation and optimization
│   │   ├── Trading/         # Live trading execution
│   │   └── Analytics/       # Performance metrics, reporting
│   ├── Configuration/       # Config models and loading
│   ├── *Runner.cs           # Mode-specific execution logic
│   └── Program.cs           # Entry point, DI container
├── ComplexBot.Tests/        # Unit tests
└── ComplexBot.Integration/  # Integration tests (Binance API)
```

## Common Tasks

### Run a backtest
```bash
cd ComplexBot
dotnet run
# Select: 1. Backtest
# Choose symbol, date range, strategy
```

### Optimize parameters
```bash
dotnet run
# Select: 2. Optimize Parameters
# Choose: Grid Search or Genetic Algorithm
```

### Analyze strategy robustness
```bash
dotnet run
# Select: 3. Walk-Forward Analysis or 4. Monte Carlo Simulation
```

### Download historical data
```bash
dotnet run
# Select: 6. Download Historical Data
```

### Paper trading on testnet
1. Configure API keys in `appsettings.json`
2. Set `UseTestnet: true` and `PaperTrade: true`
3. Run: `dotnet run`, select "5. Live Trading (Paper/Real)"

## Debugging Tips
- Set `"Logging": { "LogLevel": { "Default": "Debug" } }` in `appsettings.json`
- Use `-v detailed` with `dotnet test` for test failures
- Check `HistoricalData/` folder for cached data issues
- Verify indicator warmup periods match test expectations
- Integration tests require internet + valid testnet credentials

## Code Organization Guidelines

### Always Create Separate Files for Classes
**IMPORTANT**: Every class, interface, enum, and record should be in its own dedicated file. Never put multiple types in a single file.

**Benefits:**
- ✅ Easier to find and maintain code
- ✅ Better IDE navigation and refactoring
- ✅ Clearer namespace organization
- ✅ Reduced merge conflicts in version control
- ✅ Follows .NET naming conventions

**File naming conventions:**
- `ClassName.cs` - For classes, records, interfaces
- `EnumName.cs` - For enumerations
- `IInterfaceName.cs` - For interfaces (optional, but preferred for prominence)

**Example structure:**
```
ComplexBot/Models/
├── Records/
│   ├── Trade.cs            # One record per file
│   ├── Candle.cs
│   ├── TradeSignal.cs
│   └── ...
├── Enums/
│   ├── TradeDirection.cs   # One enum per file
│   ├── SignalType.cs
│   └── ...

ComplexBot/Services/Indicators/
├── MovingAverages/
│   ├── Ema.cs              # One indicator class per file
│   ├── Sma.cs
│   └── ...
├── Momentum/
│   ├── Macd.cs
│   ├── Rsi.cs
│   └── ...
└── ...

ComplexBot/Configuration/
├── External/
│   ├── BinanceApiSettings.cs    # One settings class per file
│   └── TelegramSettings.cs
├── Strategy/
│   ├── StrategyConfigSettings.cs
│   ├── MaStrategyConfigSettings.cs
│   └── ...
└── ...
```

**When refactoring monolithic files:**
1. Identify all distinct types (classes, records, enums, interfaces)
2. Create a dedicated file for each type in an appropriate subdirectory
3. Use logical folder names that group related types
4. Create a forwarding/aggregation file in the parent directory if needed for backward compatibility
5. Use global using statements in the aggregation file to maintain backward compatibility:
   ```csharp
   // Global imports for backward compatibility
   global using ComplexBot.Models.Enums;
   global using ComplexBot.Models.Records;
   global using TradeDirection = ComplexBot.Models.Enums.TradeDirection;
   global using Candle = ComplexBot.Models.Records.Candle;
   ```

**Valid exceptions** (where multiple types can coexist in one file):
- Closely related enum + flags enum in same file (rare)
- Abstract base + single concrete implementation (only if tightly coupled)
- Interface + minimal default implementation (only if trivial)

**Default rule**: When in doubt, create a separate file. One class per file is the standard.
